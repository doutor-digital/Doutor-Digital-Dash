using LeadAnalytics.Api.Models;

namespace LeadAnalytics.Api.Service.Ads;

/// <summary>
/// Provedor do Google Ads. STUB por enquanto (mesmo padrão do Meta). Quando as credenciais
/// (<c>Ads:Google:ClientId</c>/<c>ClientSecret</c>/<c>DeveloperToken</c>) existirem, trocar os
/// TODOs pelo OAuth2 do Google + Google Ads API (GAQL report de campaign + metrics.cost_micros).
/// </summary>
public class GoogleAdsProvider(HttpClient http, IConfiguration config, ILogger<GoogleAdsProvider> logger) : IAdsProvider
{
    private readonly HttpClient _http = http;
    private readonly ILogger<GoogleAdsProvider> _logger = logger;

    public string Provider => "google";

    private string? ClientId => config["Ads:Google:ClientId"];
    private string? ClientSecret => config["Ads:Google:ClientSecret"];
    private string? DeveloperToken => config["Ads:Google:DeveloperToken"];
    public bool IsLive =>
        !string.IsNullOrWhiteSpace(ClientId)
        && !string.IsNullOrWhiteSpace(ClientSecret)
        && !string.IsNullOrWhiteSpace(DeveloperToken);

    public string GetAuthUrl(string state, string redirectUri)
    {
        if (IsLive)
        {
            return "https://accounts.google.com/o/oauth2/v2/auth"
                 + $"?client_id={ClientId}"
                 + $"&redirect_uri={Uri.EscapeDataString(redirectUri)}"
                 + $"&state={Uri.EscapeDataString(state)}"
                 + "&response_type=code&access_type=offline&prompt=consent"
                 + "&scope=" + Uri.EscapeDataString("https://www.googleapis.com/auth/adwords");
        }
        return $"{redirectUri}?code=STUB-google&state={Uri.EscapeDataString(state)}";
    }

    public async Task<AdsTokenResult> ExchangeCodeAsync(string code, string redirectUri, CancellationToken ct)
    {
        if (IsLive && !code.StartsWith("STUB", StringComparison.Ordinal))
        {
            // TODO(real): POST https://oauth2.googleapis.com/token (code→refresh_token);
            // listar customers acessíveis (customers:listAccessibleCustomers).
            _logger.LogWarning("Troca real de token do Google ainda não implementada — usando stub.");
        }

        await Task.CompletedTask;
        return new AdsTokenResult(
            ExternalAccountId: "123-456-7890",
            AccountName: "Conta Google Ads (demo)",
            AccessToken: "stub-google-access-token",
            RefreshToken: "stub-google-refresh-token",
            ExpiresAt: DateTime.UtcNow.AddHours(1));
    }

    public async Task<IReadOnlyList<CampaignSpendRow>> FetchDailySpendAsync(
        AdAccount account, DateOnly from, DateOnly to, CancellationToken ct)
    {
        if (IsLive)
        {
            // TODO(real): GoogleAdsService.SearchStream com GAQL:
            //   SELECT campaign.id, campaign.name, metrics.cost_micros, segments.date
            //   FROM campaign WHERE segments.date BETWEEN '{from}' AND '{to}'
            //   (cost = cost_micros / 1_000_000).
            _logger.LogWarning("FetchDailySpend real do Google ainda não implementado — usando stub.");
        }

        await Task.CompletedTask;
        return StubSpend.Generate("google", from, to);
    }
}
