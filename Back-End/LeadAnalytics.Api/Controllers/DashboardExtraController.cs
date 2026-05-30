using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.Service;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LeadAnalytics.Api.Controllers;

/// <summary>
/// Endpoints extras pro dashboard: heatmap (dia x hora), campanhas (treemap),
/// evolução por unidade (multi-linha). Mantidos separados pra não engordar
/// o LeadController/WebhooksController.
/// </summary>
[ApiController]
[Authorize]
[Route("webhooks/dashboard")]
public class DashboardExtraController(
    AppDbContext db,
    TenantUnitGuard tenantGuard) : ControllerBase
{
    private readonly AppDbContext _db = db;
    private readonly TenantUnitGuard _tenantGuard = tenantGuard;

    /// <summary>
    /// Matriz 7x24 (dia da semana × hora) de criação de leads no período.
    /// Resposta plana: array de { dayOfWeek (0=dom..6=sab), hour, count }.
    /// </summary>
    [HttpGet("heatmap")]
    public async Task<IActionResult> Heatmap(
        [FromQuery] int? unitId,
        [FromQuery] DateTime? dateFrom,
        [FromQuery] DateTime? dateTo,
        [FromQuery] string? source,
        CancellationToken ct = default)
    {
        var (error, tenantId) = await _tenantGuard.ResolveTenantAsync(unitId, ct);
        if (error is not null) return error;

        var q = _db.Leads.AsNoTracking().AsQueryable();
        if (tenantId.HasValue) q = q.Where(l => l.TenantId == tenantId);
        if (unitId.HasValue) q = q.Where(l => l.UnitId == unitId);
        if (dateFrom.HasValue) q = q.Where(l => l.CreatedAt >= dateFrom);
        if (dateTo.HasValue) q = q.Where(l => l.CreatedAt <= dateTo);
        if (!string.IsNullOrWhiteSpace(source)) q = q.Where(l => l.Source == source);

        // Traz só os timestamps (barato) e agrupa em memória — pra 5-100k leads é OK.
        var times = await q.Select(l => l.CreatedAt).ToListAsync(ct);

        var grouped = times
            .GroupBy(t => new { Dow = (int)t.DayOfWeek, Hour = t.Hour })
            .Select(g => new HeatmapCellDto
            {
                DayOfWeek = g.Key.Dow,
                Hour = g.Key.Hour,
                Count = g.Count(),
            })
            .OrderBy(c => c.DayOfWeek).ThenBy(c => c.Hour)
            .ToList();

        return Ok(grouped);
    }

    /// <summary>Top N campanhas por volume (pra treemap). Default: top 30.</summary>
    [HttpGet("campaigns")]
    public async Task<IActionResult> Campaigns(
        [FromQuery] int? unitId,
        [FromQuery] DateTime? dateFrom,
        [FromQuery] DateTime? dateTo,
        [FromQuery] string? source,
        [FromQuery] int top = 30,
        CancellationToken ct = default)
    {
        if (top is < 1 or > 200) top = 30;

        var (error, tenantId) = await _tenantGuard.ResolveTenantAsync(unitId, ct);
        if (error is not null) return error;

        var q = _db.Leads.AsNoTracking().AsQueryable();
        if (tenantId.HasValue) q = q.Where(l => l.TenantId == tenantId);
        if (unitId.HasValue) q = q.Where(l => l.UnitId == unitId);
        if (dateFrom.HasValue) q = q.Where(l => l.CreatedAt >= dateFrom);
        if (dateTo.HasValue) q = q.Where(l => l.CreatedAt <= dateTo);
        if (!string.IsNullOrWhiteSpace(source)) q = q.Where(l => l.Source == source);

        var items = await q
            .Where(l => l.Campaign != null && l.Campaign != "DESCONHECIDO" && l.Campaign != "")
            .GroupBy(l => new { l.Campaign, l.Source })
            .Select(g => new CampaignCellDto
            {
                Campaign = g.Key.Campaign!,
                Source = g.Key.Source ?? "—",
                Count = g.Count(),
            })
            .OrderByDescending(x => x.Count)
            .Take(top)
            .ToListAsync(ct);

        return Ok(items);
    }

    /// <summary>
    /// Série temporal de leads por unidade (uma série por unitId), pra gráfico
    /// multi-linha de comparação. Agrupado por dia.
    /// </summary>
    [HttpGet("by-unit-evolution")]
    public async Task<IActionResult> ByUnitEvolution(
        [FromQuery] DateTime dateFrom,
        [FromQuery] DateTime dateTo,
        [FromQuery] string? source,
        CancellationToken ct = default)
    {
        if (_tenantGuard.RequireTenant(out var tenantId) is { } denied) return denied;
        if (dateTo < dateFrom) return BadRequest(new { error = "dateTo < dateFrom" });

        var q = _db.Leads.AsNoTracking()
            .Where(l => l.CreatedAt >= dateFrom && l.CreatedAt <= dateTo);
        if (tenantId.HasValue) q = q.Where(l => l.TenantId == tenantId);
        if (!string.IsNullOrWhiteSpace(source)) q = q.Where(l => l.Source == source);

        var raw = await q
            .Select(l => new { l.UnitId, Date = l.CreatedAt.Date })
            .ToListAsync(ct);

        // Lista de unidades pro nome legível
        var unitNames = await _db.Units.AsNoTracking()
            .ToDictionaryAsync(u => (int?)u.Id, u => u.Name, ct);

        // Agrupa em memória — barato
        var grouped = raw
            .GroupBy(x => new { x.UnitId, x.Date })
            .Select(g => new { g.Key.UnitId, g.Key.Date, Count = g.Count() })
            .ToList();

        var perUnit = grouped
            .GroupBy(x => x.UnitId)
            .Select(g => new ByUnitEvolutionDto
            {
                UnitId = g.Key,
                UnitName = g.Key.HasValue && unitNames.TryGetValue(g.Key, out var n) ? n : "(sem unidade)",
                Points = g.OrderBy(p => p.Date)
                          .Select(p => new ByUnitPointDto { Date = p.Date, Count = p.Count })
                          .ToList(),
            })
            .OrderByDescending(s => s.Points.Sum(p => p.Count))
            .ToList();

        return Ok(perUnit);
    }
}

public class HeatmapCellDto
{
    public int DayOfWeek { get; set; } // 0=domingo .. 6=sábado
    public int Hour { get; set; }      // 0..23
    public int Count { get; set; }
}

public class CampaignCellDto
{
    public string Campaign { get; set; } = null!;
    public string Source { get; set; } = null!;
    public int Count { get; set; }
}

public class ByUnitEvolutionDto
{
    public int? UnitId { get; set; }
    public string UnitName { get; set; } = null!;
    public List<ByUnitPointDto> Points { get; set; } = new();
}

public class ByUnitPointDto
{
    public DateTime Date { get; set; }
    public int Count { get; set; }
}
