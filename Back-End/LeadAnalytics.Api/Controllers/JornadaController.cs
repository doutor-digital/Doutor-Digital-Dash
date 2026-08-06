using LeadAnalytics.Api.Service;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LeadAnalytics.Api.Controllers;

/// <summary>
/// A jornada de um lead: busca por telefone/nome/número, linha do tempo e conversões mais rápidas.
/// </summary>
[ApiController]
[Authorize]
[Route("api/jornada")]
public class JornadaController(JornadaService jornada, TenantUnitGuard tenantGuard) : ControllerBase
{
    private readonly JornadaService _jornada = jornada;
    private readonly TenantUnitGuard _tenantGuard = tenantGuard;

    /// <summary>Acha o lead por telefone, nome, número do lead ou número na Kommo.</summary>
    [HttpGet("busca")]
    public async Task<IActionResult> Busca(
        [FromQuery] string termo, [FromQuery] int? unitId, CancellationToken ct = default)
    {
        var (error, tenantId) = await _tenantGuard.ResolveTenantAsync(unitId, ct);
        if (error is not null) return error;
        if (tenantId is not int tid) return Forbid();

        return Ok(await _jornada.BuscarAsync(tid, unitId, termo, ct));
    }

    /// <summary>Linha do tempo completa de um lead.</summary>
    [HttpGet("{leadId:int}")]
    public async Task<IActionResult> Get(
        int leadId, [FromQuery] int? unitId, CancellationToken ct = default)
    {
        var (error, tenantId) = await _tenantGuard.ResolveTenantAsync(unitId, ct);
        if (error is not null) return error;
        if (tenantId is not int tid) return Forbid();

        var dto = await _jornada.GetAsync(tid, leadId, ct);
        return dto is null ? NotFound() : Ok(dto);
    }

    /// <summary>Quem foi de lead novo a agendado no menor tempo, no período.</summary>
    [HttpGet("ranking")]
    public async Task<IActionResult> Ranking(
        [FromQuery] DateTime de, [FromQuery] DateTime ate,
        [FromQuery] int? unitId, CancellationToken ct = default)
    {
        var (error, tenantId) = await _tenantGuard.ResolveTenantAsync(unitId, ct);
        if (error is not null) return error;
        if (tenantId is not int tid) return Forbid();

        return Ok(await _jornada.RankingAsync(tid, unitId, de, ate, ct));
    }
}
