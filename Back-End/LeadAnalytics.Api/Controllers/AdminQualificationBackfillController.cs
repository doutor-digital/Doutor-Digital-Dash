using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.Service;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LeadAnalytics.Api.Controllers;

/// <summary>
/// Botão "Atualizar agora" do widget Qualificação. Roda <see cref="QualificationBackfillService"/>
/// pra UMA unidade sob demanda, sem esperar o job noturno. Útil quando o time preencheu o campo
/// "Qualificação do lead" hoje (inclusive em leads antigos) e quer ver a data REAL de
/// preenchimento no dashboard agora. O webhook ao vivo já carimba em tempo real
/// (KommoIngestionService), mas o backfill aqui pega o que veio antes do deploy ou se o webhook falhou.
///
/// Uso: POST /api/admin/qualification-backfill/{unitId}?maxPages=100
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/admin/qualification-backfill")]
public class AdminQualificationBackfillController(
    AppDbContext db,
    QualificationBackfillService backfill,
    ILogger<AdminQualificationBackfillController> logger) : ControllerBase
{
    private const int DefaultMaxPages = 100;

    [HttpPost("{unitId:int}")]
    public async Task<IActionResult> RunForUnit(int unitId, [FromQuery] int? maxPages, CancellationToken ct)
    {
        var unit = await db.Units.AsNoTracking().FirstOrDefaultAsync(u => u.Id == unitId, ct);
        if (unit is null) return NotFound(new { error = "unit não encontrada" });

        var pages = maxPages.HasValue && maxPages.Value > 0 ? Math.Min(maxPages.Value, 300) : DefaultMaxPages;
        logger.LogInformation("[qualif-backfill] manual unit={Unit} maxPages={Pages}", unitId, pages);

        var r = await backfill.BackfillUnitAsync(unit, pages, ct);
        return Ok(new
        {
            unit = new { unit.Id, unit.Name },
            scanned = r.EventsScanned,
            updated = r.Inserted,
            hitCap = r.HitCap,
            oldest = r.OldestEventUtc,
            error = r.Error,
        });
    }
}
