using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LeadAnalytics.Api.Controllers;

/// <summary>
/// CRUD das exclusões manuais de leads do KPI ("não contar").
///
/// Endpoints (todos só pra admin):
///  • GET    /api/admin/kpi-exclusions?unitId=X&kpiKey=agendados
///  • POST   /api/admin/kpi-exclusions  body { unit_id, kpi_key, lead_id, reason? }
///  • DELETE /api/admin/kpi-exclusions  body { unit_id, kpi_key, lead_id }
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/admin/kpi-exclusions")]
public class AdminKpiExclusionsController(
    AppDbContext db,
    ILogger<AdminKpiExclusionsController> logger) : ControllerBase
{
    public record ToggleBody(int UnitId, string KpiKey, int LeadId, string? Reason);

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] int unitId,
        [FromQuery] string kpiKey,
        CancellationToken ct)
    {
        var unit = await db.Units.AsNoTracking()
            .Where(u => u.Id == unitId)
            .Select(u => new { u.Id, u.ClinicId })
            .FirstOrDefaultAsync(ct);
        if (unit is null) return NotFound(new { error = "unit não encontrada" });

        var items = await db.KpiExclusions.AsNoTracking()
            .Where(e => e.TenantId == unit.ClinicId
                     && e.UnitId == unitId
                     && e.KpiKey == kpiKey)
            .OrderByDescending(e => e.ExcludedAt)
            .Select(e => new
            {
                e.Id,
                lead_id = e.LeadId,
                lead_name = e.Lead!.Name,
                e.Reason,
                excluded_at = e.ExcludedAt,
            })
            .ToListAsync(ct);

        return Ok(new { items, total = items.Count });
    }

    [HttpPost]
    public async Task<IActionResult> Add([FromBody] ToggleBody body, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(body.KpiKey)) return BadRequest(new { error = "kpi_key obrigatório" });

        var unit = await db.Units.AsNoTracking()
            .Where(u => u.Id == body.UnitId)
            .Select(u => new { u.Id, u.ClinicId })
            .FirstOrDefaultAsync(ct);
        if (unit is null) return NotFound(new { error = "unit não encontrada" });

        // Verifica que o lead pertence ao tenant — evita exclusão cross-tenant.
        var leadExists = await db.Leads.AsNoTracking()
            .AnyAsync(l => l.Id == body.LeadId && l.TenantId == unit.ClinicId, ct);
        if (!leadExists) return NotFound(new { error = "lead não pertence à unidade" });

        // Idempotente: se já existe, não faz nada (índice único garantiria, mas evita exception).
        var existing = await db.KpiExclusions
            .FirstOrDefaultAsync(e => e.TenantId == unit.ClinicId
                                   && e.UnitId == body.UnitId
                                   && e.KpiKey == body.KpiKey
                                   && e.LeadId == body.LeadId, ct);
        if (existing is not null)
            return Ok(new { id = existing.Id, already = true });

        var ex = new KpiExclusion
        {
            TenantId = unit.ClinicId,
            UnitId = body.UnitId,
            KpiKey = body.KpiKey.Trim(),
            LeadId = body.LeadId,
            Reason = string.IsNullOrWhiteSpace(body.Reason) ? null : body.Reason.Trim(),
            ExcludedAt = DateTime.UtcNow,
        };
        db.KpiExclusions.Add(ex);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("[kpi-exclusion] add unit={Unit} kpi={Kpi} lead={Lead}",
            body.UnitId, body.KpiKey, body.LeadId);
        return Ok(new { id = ex.Id, already = false });
    }

    [HttpDelete]
    public async Task<IActionResult> Remove([FromBody] ToggleBody body, CancellationToken ct)
    {
        var unit = await db.Units.AsNoTracking()
            .Where(u => u.Id == body.UnitId)
            .Select(u => new { u.Id, u.ClinicId })
            .FirstOrDefaultAsync(ct);
        if (unit is null) return NotFound(new { error = "unit não encontrada" });

        var row = await db.KpiExclusions
            .FirstOrDefaultAsync(e => e.TenantId == unit.ClinicId
                                   && e.UnitId == body.UnitId
                                   && e.KpiKey == body.KpiKey
                                   && e.LeadId == body.LeadId, ct);
        if (row is null) return NotFound(new { error = "exclusão não encontrada" });

        db.KpiExclusions.Remove(row);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("[kpi-exclusion] remove unit={Unit} kpi={Kpi} lead={Lead}",
            body.UnitId, body.KpiKey, body.LeadId);
        return Ok(new { ok = true });
    }
}
