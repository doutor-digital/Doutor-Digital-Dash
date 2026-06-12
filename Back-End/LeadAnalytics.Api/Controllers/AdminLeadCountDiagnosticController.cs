using LeadAnalytics.Api.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LeadAnalytics.Api.Controllers;

/// <summary>
/// Diagnóstico: quebra os leads do banco de uma unidade no período pra explicar
/// divergência com a Kommo. Mostra contagens por Source / Status / presença de
/// ExternalId, e devolve amostras dos leads "suspeitos" (sem ExternalId Kommo,
/// deletados, source != Kommo) pra você inspecionar.
///
/// Uso: GET /api/admin/lead-count-diagnostic/{unitId}?dateFrom=2026-06-01&dateTo=2026-06-12
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/admin/lead-count-diagnostic")]
public class AdminLeadCountDiagnosticController(AppDbContext db) : ControllerBase
{
    [HttpGet("{unitId:int}")]
    public Task<IActionResult> DiagnoseById(
        int unitId,
        [FromQuery] DateTime dateFrom,
        [FromQuery] DateTime dateTo,
        CancellationToken ct) => DiagnoseInternal(u => u.Id == unitId, $"id={unitId}", dateFrom, dateTo, ct);

    /// <summary>
    /// Mesmo diagnóstico, mas casa unidade por NOME (substring case-insensitive).
    /// Ex.: GET /api/admin/lead-count-diagnostic/by-name/imperatriz?dateFrom=...&dateTo=...
    /// </summary>
    [HttpGet("by-name/{nameLike}")]
    public Task<IActionResult> DiagnoseByName(
        string nameLike,
        [FromQuery] DateTime dateFrom,
        [FromQuery] DateTime dateTo,
        CancellationToken ct)
    {
        var pattern = $"%{nameLike}%";
        return DiagnoseInternal(u => EF.Functions.ILike(u.Name, pattern), $"name~={nameLike}", dateFrom, dateTo, ct);
    }

    private async Task<IActionResult> DiagnoseInternal(
        System.Linq.Expressions.Expression<Func<Models.Unit, bool>> match,
        string criterio,
        DateTime dateFrom, DateTime dateTo, CancellationToken ct)
    {
        var unitRow = await db.Units.AsNoTracking()
            .Where(match)
            .Select(u => new { u.Id, u.ClinicId, u.Name })
            .FirstOrDefaultAsync(ct);
        if (unitRow is null) return NotFound(new { error = $"unit não encontrada ({criterio})" });
        var unit = unitRow;

        var fromUtc = DateTime.SpecifyKind(dateFrom, DateTimeKind.Utc);
        var toUtc = DateTime.SpecifyKind(dateTo, DateTimeKind.Utc);
        var endExclUtc = toUtc.TimeOfDay == TimeSpan.Zero ? toUtc.AddDays(1) : toUtc;

        // Mesma janela do dashboard (LeadService.GetDashboardOverviewAsync:1726-1728):
        // COALESCE(original_created_at, created_at) >= from && < endExcl.
        var inWindow = db.Leads.AsNoTracking()
            .Where(l => l.TenantId == unit.ClinicId
                     && l.UnitId == unit.Id
                     && (l.OriginalCreatedAt ?? l.CreatedAt) >= fromUtc
                     && (l.OriginalCreatedAt ?? l.CreatedAt) <  endExclUtc);

        var total = await inWindow.CountAsync(ct);

        var bySource = await inWindow
            .GroupBy(l => l.Source ?? "(null)")
            .Select(g => new { source = g.Key, count = g.Count() })
            .OrderByDescending(x => x.count)
            .ToListAsync(ct);

        var byStatus = await inWindow
            .GroupBy(l => l.Status ?? "(null)")
            .Select(g => new { status = g.Key, count = g.Count() })
            .OrderByDescending(x => x.count)
            .ToListAsync(ct);

        var deleted = await inWindow.CountAsync(l => l.Status == "deleted", ct);
        var withoutKommoExternal = await inWindow.CountAsync(l => l.ExternalId == 0, ct);
        var fromKommo = await inWindow.CountAsync(l => l.Source == "Kommo" && l.ExternalId != 0 && l.Status != "deleted", ct);

        // Amostras de leads "suspeitos" — os que provavelmente justificam a divergência.
        var suspeitosBase = inWindow
            .Where(l => l.Status == "deleted" || l.ExternalId == 0 || (l.Source != null && l.Source != "Kommo"));

        var suspeitos = await suspeitosBase
            .OrderByDescending(l => l.OriginalCreatedAt ?? l.CreatedAt)
            .Take(50)
            .Select(l => new
            {
                id = l.Id,
                external_id = l.ExternalId,
                name = l.Name,
                phone = l.Phone,
                source = l.Source,
                status = l.Status,
                created_at = l.CreatedAt,
                original_created_at = l.OriginalCreatedAt,
                why = (l.Status == "deleted" ? "deleted" : null)
                   ?? (l.ExternalId == 0 ? "sem-external-id" : null)
                   ?? (l.Source != null && l.Source != "Kommo" ? $"source={l.Source}" : null)
                   ?? "?",
            })
            .ToListAsync(ct);

        return Ok(new
        {
            unit = new { unit.Id, unit.Name },
            window = new { from = fromUtc, to = endExclUtc },
            summary = new
            {
                total_no_banco = total,
                kommo_validos = fromKommo,
                provavel_diferenca = total - fromKommo,
                deletados = deleted,
                sem_external_id_kommo = withoutKommoExternal,
            },
            by_source = bySource,
            by_status = byStatus,
            suspeitos_amostra = suspeitos,
            hint = "Compare `kommo_validos` com o número da Kommo. `provavel_diferenca` é o overhead. Amostra mostra quem está sobrando.",
        });
    }
}
