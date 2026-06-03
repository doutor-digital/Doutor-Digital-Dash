using LeadAnalytics.Api.Models;

namespace LeadAnalytics.Api.Service.Ads;

/// <summary>
/// Provedor do Google Ads. STUB enquanto não há credenciais reais. Com credenciais
/// (ClientId/ClientSecret/DeveloperToken), trocar os TODOs pelo OAuth2 do Google + Google
/// Ads API (GAQL report de campaign + metrics.cost_micros).
/// </summary>
public class GoogleAdsProvider(HttpClient http, ILogger<GoogleAdsProvider> logger) : IAdsProvider
{
    private readonly HttpClient _http = http;
    private readonly ILogger<GoogleAdsProvider> _logger = logger;

    public string Provider => "google";

    public string GetAuthUrl(AdsCredentials creds, string state, string redirectUri)
    {
        if (creds.IsConfigured)
        {
            return "https://accounts.google.com/o/oauth2/v2/auth"
                 + $"?client_id={creds.ClientId}"
                 + $"&redirect_uri={Uri.EscapeDataString(redirectUri)}"
                 + $"&state={Uri.EscapeDataString(state)}"
                 + "&response_type=code&access_type=offline&prompt=consent"
                 + "&scope=" + Uri.EscapeDataString("https://www.googleapis.com/auth/adwords");
        }
        return $"{redirectUri}?code=STUB-google&state={Uri.EscapeDataString(state)}";
    }

    public async Task<AdsTokenResult> ExchangeCodeAsync(AdsCredentials creds, string code, string redirectUri, CancellationToken ct)
    {
        if (creds.IsConfigured && !code.StartsWith("STUB", StringComparison.Ordinal))
        {
            // TODO(real): POST https://oauth2.googleapis.com/token (code→refresh_token);
            // customers:listAccessibleCustomers pra achar o customer id.
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
        AdsCredentials creds, AdAccount account, DateOnly from, DateOnly to, CancellationToken ct)
    {
        if (creds.IsConfigured)
        {
            // TODO(real): GoogleAdsService.SearchStream com GAQL:
            //   SELECT campaign.id, campaign.name, metrics.cost_micros, segments.date
            //   FROM campaign WHERE segments.date BETWEEN '{from}' AND '{to}'  (cost = cost_micros/1e6).
            _logger.LogWarning("FetchDailySpend real do Google ainda não implementado — usando stub.");
        }

        await Task.CompletedTask;
        return StubSpend.Generate("google", from, to);
    }
}
