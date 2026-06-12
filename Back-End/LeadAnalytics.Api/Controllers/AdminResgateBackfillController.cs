using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.Service;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LeadAnalytics.Api.Controllers;

/// <summary>
/// Botão "Atualizar agora" do KPI Resgate. Roda <see cref="ResgateAttemptBackfillService"/>
/// pra UMA unidade sob demanda, sem esperar o job noturno de 24h. Útil quando o time
/// preencheu "Tentativas de resgastes" hoje e quer ver o número no dashboard agora.
/// O webhook ao vivo já grava em tempo real (KommoIngestionService.UpsertRecoveryAttemptsAsync),
/// mas o backfill aqui pega o que veio antes do deploy ou se o webhook falhou.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/admin/resgate-backfill")]
public class AdminResgateBackfillController(
    AppDbContext db,
    ResgateAttemptBackfillService backfill,
    ILogger<AdminResgateBackfillController> logger) : ControllerBase
{
    private const int DefaultMaxPages = 100;

    [HttpPost("{unitId:int}")]
    public async Task<IActionResult> RunForUnit(int unitId, [FromQuery] int? maxPages, CancellationToken ct)
    {
        var unit = await db.Units.AsNoTracking().FirstOrDefaultAsync(u => u.Id == unitId, ct);
        if (unit is null) return NotFound(new { error = "unit não encontrada" });

        var pages = maxPages.HasValue && maxPages.Value > 0 ? Math.Min(maxPages.Value, 300) : DefaultMaxPages;
        logger.LogInformation("[resgate-backfill] manual unit={Unit} maxPages={Pages}", unitId, pages);

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
