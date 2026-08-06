using LeadAnalytics.Api.DTOs.Ads;
using LeadAnalytics.Api.Service;
using LeadAnalytics.Api.Service.Ads;
using Microsoft.EntityFrameworkCore;
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
    InternalApiKeyGuard guard,
    Data.AppDbContext db,
    ProtectedTokenService tokens) : ControllerBase
{
    private readonly AdsSpendSyncService _adsSync = adsSync;
    private readonly InternalApiKeyGuard _guard = guard;
    private readonly Data.AppDbContext _db = db;
    private readonly ProtectedTokenService _tokens = tokens;

    /// <summary>
    /// Registra (ou atualiza) a conta de anúncios de uma unidade com um token de Usuário do
    /// Sistema, sem passar pelo OAuth.
    ///
    /// POR QUE ISTO EXISTE
    /// -------------------
    /// O fluxo OAuth depende do app da Meta estar publicado e com o domínio autorizado — e
    /// ficou bloqueado por meses por revisão de política, deixando o dashboard sem nome de
    /// campanha e sem custo por lead. Token de Usuário do Sistema não expira e não depende de
    /// aprovação de app; é o caminho que a própria Meta recomenda para integração de servidor.
    ///
    /// Protegido pela mesma chave interna do resto: nunca é chamado pelo navegador.
    /// </summary>
    [HttpPost("account")]
    public async Task<IActionResult> RegistrarConta(
        [FromHeader(Name = "X-Admin-Key")] string? adminKey,
        [FromBody] RegistrarContaAdsRequest req,
        CancellationToken ct)
    {
        if (!await _guard.IsAuthorizedAsync(adminKey))
            return Unauthorized(new { message = "Acesso negado" });

        if (req.ClinicId <= 0 || string.IsNullOrWhiteSpace(req.ExternalAccountId)
            || string.IsNullOrWhiteSpace(req.AccessToken))
            return BadRequest(new { message = "clinicId, externalAccountId e accessToken são obrigatórios" });

        var provider = string.IsNullOrWhiteSpace(req.Provider) ? "meta" : req.Provider.Trim().ToLowerInvariant();

        // Uma conta por unidade (ou por clínica, quando a unidade não é informada): a chave é a
        // mesma que o AdCreativeService usa para achar o token.
        var conta = await _db.AdAccounts.FirstOrDefaultAsync(
            a => a.ClinicId == req.ClinicId && a.Provider == provider && a.UnitId == req.UnitId, ct);

        var agora = DateTime.UtcNow;
        if (conta is null)
        {
            conta = new Models.AdAccount
            {
                ClinicId = req.ClinicId,
                UnitId = req.UnitId,
                Provider = provider,
                CreatedAt = agora,
            };
            _db.AdAccounts.Add(conta);
        }

        conta.ExternalAccountId = req.ExternalAccountId.Trim().Replace("act_", "");
        conta.Name = req.Name;
        conta.Status = "connected";
        conta.AccessTokenEnc = _tokens.Protect(req.AccessToken.Trim());
        // Token de Usuário do Sistema não expira; deixar a data nula evita alarme falso de
        // "token vencido" numa credencial que é permanente.
        conta.TokenExpiresAt = null;
        conta.UpdatedByEmail = req.UpdatedByEmail ?? "internal/ads/account";
        conta.UpdatedAt = agora;

        await _db.SaveChangesAsync(ct);

        return Ok(new { conta.Id, conta.ClinicId, conta.UnitId, conta.Provider, conta.ExternalAccountId, conta.Name });
    }

    public record RegistrarContaAdsRequest(
        int ClinicId,
        int? UnitId,
        string? Provider,
        string ExternalAccountId,
        string AccessToken,
        string? Name,
        string? UpdatedByEmail);

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
