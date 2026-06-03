using LeadAnalytics.Api.Models;

namespace LeadAnalytics.Api.Service.Ads;

/// <summary>
/// Provedor do Meta Ads. STUB por enquanto: o fluxo OAuth funciona ponta a ponta (o
/// "Conectar" volta direto pro callback) e o gasto é mock. Quando as credenciais
/// (<c>Ads:Meta:AppId</c>/<c>AppSecret</c>) existirem, trocar os TODOs pelas chamadas reais
/// (Graph API: oauth/access_token + act_{id}/insights).
/// </summary>
public class MetaAdsProvider(HttpClient http, IConfiguration config, ILogger<MetaAdsProvider> logger) : IAdsProvider
{
    private readonly HttpClient _http = http;
    private readonly ILogger<MetaAdsProvider> _logger = logger;

    public string Provider => "meta";

    private string? AppId => config["Ads:Meta:AppId"];
    private string? AppSecret => config["Ads:Meta:AppSecret"];
    public bool IsLive => !string.IsNullOrWhiteSpace(AppId) && !string.IsNullOrWhiteSpace(AppSecret);

    public string GetAuthUrl(string state, string redirectUri)
    {
        if (IsLive)
        {
            // Fluxo OAuth real do Meta (Login do Facebook → consentimento ads_read).
            return "https://www.facebook.com/v19.0/dialog/oauth"
                 + $"?client_id={AppId}"
                 + $"&redirect_uri={Uri.EscapeDataString(redirectUri)}"
                 + $"&state={Uri.EscapeDataString(state)}"
                 + "&scope=ads_read,business_management";
        }
        // STUB: pula a tela do Meta e volta direto pro nosso callback com um code fake.
        return $"{redirectUri}?code=STUB-meta&state={Uri.EscapeDataString(state)}";
    }

    public async Task<AdsTokenResult> ExchangeCodeAsync(string code, string redirectUri, CancellationToken ct)
    {
        if (IsLive && !code.StartsWith("STUB", StringComparison.Ordinal))
        {
            // TODO(real): GET https://graph.facebook.com/v19.0/oauth/access_token
            //   ?client_id&client_secret&redirect_uri&code  → access_token (long-lived)
            // depois GET /me/adaccounts → ExternalAccountId + Name.
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
        AdAccount account, DateOnly from, DateOnly to, CancellationToken ct)
    {
        if (IsLive)
        {
            // TODO(real): GET /v19.0/{act_id}/insights?level=campaign&time_increment=1
            //   &fields=campaign_id,campaign_name,spend&time_range={from,to}  (paginar).
            _logger.LogWarning("FetchDailySpend real do Meta ainda não implementado — usando stub.");
        }

        await Task.CompletedTask;
        return StubSpend.Generate("meta", from, to);
    }
}
