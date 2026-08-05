using LeadAnalytics.Api.Service;
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
public class SaudeController(SaudeService saude, TenantUnitGuard tenantGuard) : ControllerBase
{
    private readonly SaudeService _saude = saude;
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
}
