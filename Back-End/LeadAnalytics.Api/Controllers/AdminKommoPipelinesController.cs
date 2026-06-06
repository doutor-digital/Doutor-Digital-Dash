using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.Service;
using LeadAnalytics.Api.Service.Ai;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LeadAnalytics.Api.Controllers;

/// <summary>
/// Lista os pipelines/stages da Kommo de uma unidade e permite escolher
/// qual <c>stage_id</c> é a "Etapa de Entrada" — usado pelo contador da I.A.
/// pra contar SÓ os leads que realmente entraram (não os movidos entre etapas).
///
/// ⚠️ TEMP: [AllowAnonymous] pra você conseguir configurar agora pelo navegador.
/// Reverter pra [Authorize] junto com os outros endpoints de debug.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/admin/kommo-pipelines")]
public class AdminKommoPipelinesController(
    AppDbContext db,
    KommoApiClient kommoApi,
    UnitEntryStageConfig entryStageConfig,
    ILogger<AdminKommoPipelinesController> logger) : ControllerBase
{
    /// <summary>
    /// Lista pipelines + stages da Kommo da unidade, e mostra qual stage
    /// está marcado como "Etapa de Entrada" atualmente (se houver).
    /// </summary>
    [HttpGet("{unitId:int}")]
    public async Task<IActionResult> GetPipelines(int unitId, CancellationToken ct)
    {
        var unit = await db.Units.AsNoTracking().FirstOrDefaultAsync(u => u.Id == unitId, ct);
        if (unit is null) return NotFound(new { error = "unit não encontrada" });
        if (string.IsNullOrWhiteSpace(unit.KommoSubdomain) || string.IsNullOrWhiteSpace(unit.KommoAccessToken))
            return BadRequest(new { error = "unit sem Kommo configurado" });

        var currentEntryStageId = await entryStageConfig.GetAsync(unitId, ct);

        try
        {
            var resp = await kommoApi.GetPipelinesAsync(unit.KommoSubdomain!, unit.KommoAccessToken!, ct);
            var pipelines = resp?.Embedded?.Pipelines?.Select(p => new
            {
                id = p.Id,
                name = p.Name,
                is_main = p.IsMain,
                sort = p.Sort,
                stages = p.Embedded?.Statuses?.OrderBy(s => s.Sort).Select(s => new
                {
                    id = s.Id,
                    name = s.Name,
                    sort = s.Sort,
                    color = s.Color,
                    is_entry_stage = s.Id == currentEntryStageId,
                }),
            }).ToList();

            return Ok(new
            {
                unit = new { unit.Id, unit.Name, unit.KommoSubdomain },
                currentEntryStageId,
                pipelines,
                hint = "Pra setar a etapa de entrada: PUT /api/admin/kommo-pipelines/{unitId}/entry-stage com body { stageId: <id> }",
            });
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[admin-pipelines] erro buscando pipelines da unit {Unit}", unitId);
            return Ok(new { error = ex.Message });
        }
    }

    public record SetEntryStageRequest(int StageId);

    [HttpPut("{unitId:int}/entry-stage")]
    public async Task<IActionResult> SetEntryStage(int unitId, [FromBody] SetEntryStageRequest body, CancellationToken ct)
    {
        if (body.StageId <= 0) return BadRequest(new { error = "stageId inválido" });
        var unit = await db.Units.AsNoTracking().FirstOrDefaultAsync(u => u.Id == unitId, ct);
        if (unit is null) return NotFound();

        await entryStageConfig.SetAsync(unitId, body.StageId, ct);
        logger.LogInformation("[admin-pipelines] unit={Unit} entry_stage_id={Stage}", unitId, body.StageId);
        return Ok(new { unitId, entryStageId = body.StageId });
    }

    [HttpDelete("{unitId:int}/entry-stage")]
    public async Task<IActionResult> DeleteEntryStage(int unitId, CancellationToken ct)
    {
        await entryStageConfig.DeleteAsync(unitId, ct);
        return Ok(new { unitId, entryStageId = (int?)null });
    }
}
