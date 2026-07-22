using LeadAnalytics.Api.DTOs.Ads;
using LeadAnalytics.Api.Service;
using LeadAnalytics.Api.Service.Ads;
using Microsoft.AspNetCore.Mvc;

namespace LeadAnalytics.Api.Controllers;

/// <summary>
/// Ingestão de gasto de Ads vinda do n8n. O n8n autentica no Meta (token de
/// System User do Business Manager), puxa o gasto do Graph e POSTa aqui já
/// pronto; a API resolve a conta e grava. Protegido por X-Admin-Key.
/// </summary>
[ApiController]
[Route("internal/ads")]
public class InternalAdsController(
    AdsSpendSyncService adsSync,
    InternalApiKeyGuard guard) : ControllerBase
{
    private readonly AdsSpendSyncService _adsSync = adsSync;
    private readonly InternalApiKeyGuard _guard = guard;

    /// <summary>
    /// Recebe o gasto por campanha/dia de UMA conta e faz upsert em
    /// CampaignDailySpend. Resolve a conta por Provider + ExternalAccountId.
    /// </summary>
    [HttpPost("spend")]
    public async Task<IActionResult> IngestSpend(
        [FromHeader(Name = "X-Admin-Key")] string? adminKey,
        [FromBody] AdsSpendIngestRequest req,
        CancellationToken ct)
    {
        if (!await _guard.IsAuthorizedAsync(adminKey))
            return Unauthorized(new { message = "Acesso negado" });

        if (string.IsNullOrWhiteSpace(req.ExternalAccountId))
            return BadRequest(new { message = "externalAccountId obrigatório" });

        var result = await _adsSync.IngestDailySpendAsync(req, ct);

        // Conta não mapeada não é erro: devolve 200 com matched=false pro n8n
        // seguir o loop e você ver quais contas faltam mapear (AdAccount.ExternalAccountId).
        return Ok(result);
    }
}
