using LeadAnalytics.Api.Service;
using Microsoft.AspNetCore.Mvc;

namespace LeadAnalytics.Api.Controllers;

/// <summary>
/// Auditoria dos cards do painel: o número, a conferência por outro caminho e a
/// lista nominal de quem explica a diferença.
///
/// Serve pra responder "por que o card diz 2 se foram 5?" sem abrir lead por
/// lead — e pra dar à gestora a lista exata do que a SDR precisa corrigir.
/// </summary>
[ApiController]
[Route("internal/audit")]
public class AuditoriaKpiController(
    AuditoriaKpiService auditoria,
    InternalApiKeyGuard guard,
    ILogger<AuditoriaKpiController> logger) : ControllerBase
{
    [HttpGet("kpis")]
    public async Task<IActionResult> Kpis(
        [FromHeader(Name = "X-Admin-Key")] string? adminKey,
        [FromQuery] int unitId,
        [FromQuery] DateOnly? de,
        [FromQuery] DateOnly? ate,
        CancellationToken ct = default)
    {
        if (!await guard.IsAuthorizedAsync(adminKey)) return Unauthorized();
        if (unitId <= 0) return BadRequest(new { erro = "informe unitId" });

        // Janela padrão: o mês corrente até hoje — o recorte que a gestora usa.
        var hoje = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(-3));
        var inicio = de ?? new DateOnly(hoje.Year, hoje.Month, 1);
        var fim = ate ?? hoje;

        // As colunas são timestamptz: o Npgsql recusa DateTime sem Kind definido.
        var deDt = DateTime.SpecifyKind(inicio.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
        var ateDt = DateTime.SpecifyKind(fim.AddDays(1).ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);

        var blocos = await auditoria.TudoAsync(unitId, deDt, ateDt, ct);
        var totalProblemas = blocos.Sum(b => b.Divergentes.Count);

        logger.LogInformation(
            "🔎 Auditoria de KPIs | unidade={Unit} janela={De}..{Ate} divergências={Qtd}",
            unitId, inicio, fim, totalProblemas);

        return Ok(new
        {
            unitId,
            de = inicio,
            ate = fim,
            totalDivergencias = totalProblemas,
            blocos,
        });
    }
}
