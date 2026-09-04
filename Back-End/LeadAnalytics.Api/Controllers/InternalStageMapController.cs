using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.Service;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LeadAnalytics.Api.Controllers;

/// <summary>
/// Mapa de etapas da Kommo — a base para o histórico voltar a ser confiável.
///
/// O histórico guarda o id da etapa (estável) e o nome do momento (que envelhece
/// ou vem como número). Aqui a gente sincroniza id→nome de cada conta e reescreve
/// os rótulos velhos. O id nunca é tocado.
/// </summary>
[ApiController]
[Route("internal/stage-map")]
public class InternalStageMapController(
    AppDbContext db,
    KommoStageMapService mapa,
    InternalApiKeyGuard guard) : ControllerBase
{
    /// <summary>Relê os funis da Kommo e regrava o mapa. Sem unitId, roda em todas.</summary>
    [HttpPost("sync")]
    public async Task<IActionResult> Sync(
        [FromHeader(Name = "X-Admin-Key")] string? adminKey,
        [FromQuery] int? unitId,
        CancellationToken ct = default)
    {
        if (!await guard.IsAuthorizedAsync(adminKey)) return Unauthorized();

        var unidades = unitId.HasValue
            ? [unitId.Value]
            : await db.Units.AsNoTracking()
                .Where(u => u.IsActive && u.KommoAccessToken != null && u.KommoSubdomain != null)
                .Select(u => u.Id).ToListAsync(ct);

        var saida = new List<KommoStageMapService.ResultadoSync>();
        foreach (var id in unidades) saida.Add(await mapa.SincronizarAsync(id, ct));
        return Ok(new { unidades = saida.Count, resultado = saida });
    }

    /// <summary>
    /// Reescreve o rótulo do histórico pelo mapa. Padrão: simulação e 90 dias.
    /// </summary>
    [HttpPost("corrigir-rotulos")]
    public async Task<IActionResult> CorrigirRotulos(
        [FromHeader(Name = "X-Admin-Key")] string? adminKey,
        [FromQuery] int? unitId,
        [FromQuery] int dias = 90,
        [FromQuery] bool simular = true,
        CancellationToken ct = default)
    {
        if (!await guard.IsAuthorizedAsync(adminKey)) return Unauthorized();

        var unidades = unitId.HasValue
            ? [unitId.Value]
            : await db.Units.AsNoTracking().Where(u => u.IsActive).Select(u => u.Id).ToListAsync(ct);

        var saida = new List<KommoStageMapService.ResultadoBackfill>();
        foreach (var id in unidades)
        {
            var r = await mapa.CorrigirRotulosAsync(id, dias, simular, ct);
            if (r.Examinados > 0) saida.Add(r);
        }

        return Ok(new
        {
            simular,
            dias,
            totalCorrigidos = saida.Sum(x => x.Corrigidos),
            totalSemMapa = saida.Sum(x => x.SemMapa),
            porUnidade = saida.OrderByDescending(x => x.Corrigidos),
        });
    }

    /// <summary>Quanto do histórico ainda está com rótulo podre — o placar do conserto.</summary>
    [HttpGet("saude")]
    public async Task<IActionResult> Saude(
        [FromHeader(Name = "X-Admin-Key")] string? adminKey,
        [FromQuery] int dias = 60,
        CancellationToken ct = default)
    {
        if (!await guard.IsAuthorizedAsync(adminKey)) return Unauthorized();

        var desde = DateTime.UtcNow.AddDays(-dias);
        var total = await db.LeadStageHistories.CountAsync(h => h.ChangedAt >= desde, ct);
        var crus = await db.LeadStageHistories
            // Npgsql traduz Regex.IsMatch para o operador ~ do Postgres; All(char.IsDigit)
            // não tem tradução e explodia em runtime.
            .CountAsync(h => h.ChangedAt >= desde && h.StageLabel != null
                && System.Text.RegularExpressions.Regex.IsMatch(h.StageLabel, "^[0-9]+$"), ct);
        var rotulos = await db.LeadStageHistories
            .Where(h => h.ChangedAt >= desde).Select(h => h.StageLabel).Distinct().CountAsync(ct);
        var etapasNoMapa = await db.KommoStages.CountAsync(ct);

        return Ok(new
        {
            dias,
            registros = total,
            comIdCruNoRotulo = crus,
            percentualPodre = total == 0 ? 0 : Math.Round(100.0 * crus / total, 1),
            rotulosDistintos = rotulos,
            etapasNoMapa,
        });
    }
}
