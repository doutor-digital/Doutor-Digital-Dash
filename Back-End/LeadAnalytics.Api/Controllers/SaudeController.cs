using LeadAnalytics.Api.Service;
using LeadAnalytics.Api.Service.Spine;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LeadAnalytics.Api.Controllers;

/// <summary>
/// Saúde das fontes que alimentam o dashboard.
///
/// Existe porque em 05/08/2026 o sync da Kommo estava parado havia 13 dias, em todas as
/// unidades, e o dashboard seguiu mostrando os números de 22/07 com a mesma cara de sempre.
/// Número velho com aparência de novo leva a decisão errada com confiança.
/// </summary>
[ApiController]
[Authorize]
[Route("api/saude")]
public class SaudeController(SaudeService saude, AgendaDoDiaService agenda, HigieneService higiene, FilasService filas,
    AtividadeService atividade, TenantUnitGuard tenantGuard) : ControllerBase
{
    private readonly SaudeService _saude = saude;
    private readonly AgendaDoDiaService _agenda = agenda;
    private readonly HigieneService _higiene = higiene;
    private readonly FilasService _filas = filas;
    private readonly AtividadeService _atividade = atividade;
    private readonly TenantUnitGuard _tenantGuard = tenantGuard;

    /// <summary>Frescor por fonte: Kommo, franquia e Meta Ads.</summary>
    [HttpGet("fontes")]
    public async Task<IActionResult> Fontes([FromQuery] int? unitId, CancellationToken ct = default)
    {
        var (error, tenantId) = await _tenantGuard.ResolveTenantAsync(unitId, ct);
        if (error is not null) return error;
        if (tenantId is not int tid) return Forbid();

        return Ok(await _saude.GetAsync(tid, unitId, ct));
    }

    /// <summary>
    /// O que a clínica tem marcado no dia, e o que a Kommo diz do mesmo dia.
    /// Padrão: hoje.
    /// </summary>
    [HttpGet("agenda-do-dia")]
    public async Task<IActionResult> AgendaDoDia(
        [FromQuery] int? unitId, [FromQuery] DateOnly? dia, CancellationToken ct = default)
    {
        var (error, tenantId) = await _tenantGuard.ResolveTenantAsync(unitId, ct);
        if (error is not null) return error;
        if (tenantId is not int tid) return Forbid();

        var alvo = dia ?? DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, SpineApiClient.BrTz));

        return Ok(await _agenda.GetAsync(tid, unitId, alvo, ct));
    }

    /// <summary>
    /// Higiene da base e sanidade da configuração — o que infla ou zera número em silêncio.
    /// </summary>
    [HttpGet("higiene")]
    public async Task<IActionResult> Higiene([FromQuery] int? unitId, CancellationToken ct = default)
    {
        var (error, tenantId) = await _tenantGuard.ResolveTenantAsync(unitId, ct);
        if (error is not null) return error;
        if (tenantId is not int tid) return Forbid();

        return Ok(await _higiene.GetAsync(tid, unitId, ct));
    }

    /// <summary>O que precisa de alguém agora: sem resposta, sem data, amanhã, faltou ontem.</summary>
    [HttpGet("filas")]
    public async Task<IActionResult> Filas([FromQuery] int? unitId, CancellationToken ct = default)
    {
        var (error, tenantId) = await _tenantGuard.ResolveTenantAsync(unitId, ct);
        if (error is not null) return error;
        if (tenantId is not int tid) return Forbid();

        return Ok(await _filas.GetAsync(tid, unitId, ct));
    }

    /// <summary>
    /// O que aconteceu no CRM nas últimas 24 h, na ordem em que aconteceu.
    /// Prova bruta por trás dos números da página.
    /// </summary>
    [HttpGet("atividade")]
    public async Task<IActionResult> Atividade(
        [FromQuery] int? unitId, [FromQuery] int limite = 30, CancellationToken ct = default)
    {
        var (error, tenantId) = await _tenantGuard.ResolveTenantAsync(unitId, ct);
        if (error is not null) return error;
        if (tenantId is not int tid) return Forbid();

        return Ok(await _atividade.GetAsync(tid, unitId, limite, ct));
    }
}
