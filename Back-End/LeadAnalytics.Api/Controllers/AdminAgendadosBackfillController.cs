using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.Service;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LeadAnalytics.Api.Controllers;

/// <summary>
/// Botão "Atualizar" do card Agendados. Roda <see cref="KommoStageHistoryBackfillService"/>
/// pra UMA unidade sob demanda, sem esperar o job noturno de 24h. Puxa os eventos
/// lead_status_changed da API da Kommo e grava LeadStageHistory(EntrySource=events_api)
/// com a data REAL de entrada na etapa — único caminho que alimenta o KPI Agendados
/// (linhas "legacy" datadas por updated_at são excluídas das contagens).
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/admin/agendados-backfill")]
public class AdminAgendadosBackfillController(
    AppDbContext db,
    KommoStageHistoryBackfillService backfill,
    ILogger<AdminAgendadosBackfillController> logger) : ControllerBase
{
    private const int DefaultMaxPages = 100;

    [HttpPost("{unitId:int}")]
    public async Task<IActionResult> RunForUnit(int unitId, [FromQuery] int? maxPages, CancellationToken ct)
    {
        var unit = await db.Units.AsNoTracking().FirstOrDefaultAsync(u => u.Id == unitId, ct);
        if (unit is null) return NotFound(new { error = "unit não encontrada" });

        var pages = maxPages.HasValue && maxPages.Value > 0 ? Math.Min(maxPages.Value, 300) : DefaultMaxPages;
        logger.LogInformation("[agendados-backfill] manual unit={Unit} maxPages={Pages}", unitId, pages);

        var r = await backfill.BackfillUnitAsync(unit, pages, ct);
        return Ok(new
        {
            unit = new { unit.Id, unit.Name },
            scanned = r.EventsScanned,
            inserted = r.Inserted,
            hitCap = r.HitCap,
            oldest = r.OldestEventUtc,
            error = r.Error,
        });
    }
}
