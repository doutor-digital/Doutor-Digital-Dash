using LeadAnalytics.Api.Models;

namespace LeadAnalytics.Api.Service.Ads;

/// <summary>
/// Provedor do Meta Ads. STUB enquanto não há credenciais reais (<see cref="AdsCredentials.IsConfigured"/>
/// = false): o fluxo OAuth funciona ponta a ponta (o "Conectar" volta direto pro callback) e o gasto
/// é mock. Com credenciais, trocar os TODOs pelas chamadas reais (Graph API: oauth/access_token +
/// act_{id}/insights).
/// </summary>
public class MetaAdsProvider(HttpClient http, ILogger<MetaAdsProvider> logger) : IAdsProvider
{
    private readonly HttpClient _http = http;
    private readonly ILogger<MetaAdsProvider> _logger = logger;

    public string Provider => "meta";

    public string GetAuthUrl(AdsCredentials creds, string state, string redirectUri)
    {
        if (creds.IsConfigured)
        {
            return "https://www.facebook.com/v19.0/dialog/oauth"
                 + $"?client_id={creds.ClientId}"
                 + $"&redirect_uri={Uri.EscapeDataString(redirectUri)}"
                 + $"&state={Uri.EscapeDataString(state)}"
                 + "&scope=ads_read,business_management";
        }
        // STUB: pula a tela do Meta e volta direto pro nosso callback com um code fake.
        return $"{redirectUri}?code=STUB-meta&state={Uri.EscapeDataString(state)}";
    }

    public async Task<AdsTokenResult> ExchangeCodeAsync(AdsCredentials creds, string code, string redirectUri, CancellationToken ct)
    {
        if (creds.IsConfigured && !code.StartsWith("STUB", StringComparison.Ordinal))
        {
            // TODO(real): GET https://graph.facebook.com/v19.0/oauth/access_token
            //   ?client_id={creds.ClientId}&client_secret={creds.ClientSecret}&redirect_uri&code
            //   → access_token (trocar por long-lived); GET /me/adaccounts → conta.
            _logger.LogWarning("Troca real de token do Meta ainda não implementada — usando stub.");
        }

        await Task.CompletedTask;
        return new AdsTokenResult(
            ExternalAccountId: "act_DEMO_META",
            AccountName: "Conta Meta Ads (demo)",
            AccessToken: "stub-meta-access-token",
            RefreshToken: null,
            ExpiresAt: DateTime.UtcNow.AddDays(60));
    }

    public async Task<IReadOnlyList<CampaignSpendRow>> FetchDailySpendAsync(
        AdsCredentials creds, AdAccount account, DateOnly from, DateOnly to, CancellationToken ct)
    {
        if (creds.IsConfigured)
        {
            // TODO(real): GET /v19.0/{act_id}/insights?level=campaign&time_increment=1
            //   &fields=campaign_id,campaign_name,spend&time_range={from,to}  (paginar).
            _logger.LogWarning("FetchDailySpend real do Meta ainda não implementado — usando stub.");
        }

        await Task.CompletedTask;
        return StubSpend.Generate("meta", from, to);
    }
}
