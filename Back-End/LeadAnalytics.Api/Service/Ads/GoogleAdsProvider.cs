using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using LeadAnalytics.Api.Models;
using LeadAnalytics.Api.Service;

namespace LeadAnalytics.Api.Service.Ads;

/// <summary>
/// Provedor do Google Ads. Sem credenciais opera em STUB. Com credenciais (ClientId/Secret/
/// DeveloperToken), usa OAuth2 + Google Ads API REST: troca code→refresh_token, descobre o
/// customer id e roda um GAQL (campaign + metrics.cost_micros + segments.date) via searchStream.
/// </summary>
public class GoogleAdsProvider(HttpClient http, ProtectedTokenService tokens, ILogger<GoogleAdsProvider> logger) : IAdsProvider
{
    private const string ApiVersion = "v17";
    private const string TokenUrl = "https://oauth2.googleapis.com/token";
    private const string AdsBase = "https://googleads.googleapis.com/" + ApiVersion;

    private readonly HttpClient _http = http;
    private readonly ProtectedTokenService _tokens = tokens;
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
        if (!creds.IsConfigured || code.StartsWith("STUB", StringComparison.Ordinal))
        {
            return new AdsTokenResult("123-456-7890", "Conta Google Ads (demo)", "stub-google-access-token", "stub-google-refresh-token", DateTime.UtcNow.AddHours(1));
        }

        // 1) code → refresh_token + access_token.
        using var tokenDoc = await PostFormAsync(TokenUrl, new Dictionary<string, string>
        {
            ["code"] = code,
            ["client_id"] = creds.ClientId!,
            ["client_secret"] = creds.ClientSecret!,
            ["redirect_uri"] = redirectUri,
            ["grant_type"] = "authorization_code",
        }, null, ct);
        var accessToken = GetStr(tokenDoc, "access_token");
        var refreshToken = GetStr(tokenDoc, "refresh_token");
        var expiresIn = tokenDoc.RootElement.TryGetProperty("expires_in", out var ei) && ei.TryGetInt32(out var s) ? s : 3600;
        if (string.IsNullOrEmpty(accessToken) || string.IsNullOrEmpty(refreshToken))
            throw new InvalidOperationException("Google não retornou refresh_token (use prompt=consent + access_type=offline).");

        // 2) primeiro customer acessível.
        var customerId = await FirstAccessibleCustomerAsync(creds, accessToken!, ct);

        return new AdsTokenResult(customerId, $"Google Ads {customerId}", accessToken!, refreshToken!, DateTime.UtcNow.AddSeconds(expiresIn));
    }

    public async Task<IReadOnlyList<CampaignSpendRow>> FetchDailySpendAsync(
        AdsCredentials creds, AdAccount account, DateOnly from, DateOnly to, CancellationToken ct)
    {
        if (!creds.IsConfigured)
            return StubSpend.Generate("google", from, to);

        var refreshToken = _tokens.Unprotect(account.RefreshTokenEnc);
        var customerId = (account.ExternalAccountId ?? "").Replace("-", "");
        if (string.IsNullOrEmpty(refreshToken) || customerId.Length == 0)
        {
            _logger.LogWarning("Conta Google {Id} sem refresh_token/customer — pulando.", account.Id);
            return Array.Empty<CampaignSpendRow>();
        }

        // Access token fresco a partir do refresh_token (expira em ~1h).
        var accessToken = await RefreshAccessTokenAsync(creds, refreshToken, ct);
        if (accessToken is null) return Array.Empty<CampaignSpendRow>();

        var since = from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var until = to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var gaql =
            "SELECT campaign.id, campaign.name, metrics.cost_micros, segments.date " +
            $"FROM campaign WHERE segments.date BETWEEN '{since}' AND '{until}'";

        var url = $"{AdsBase}/customers/{customerId}/googleAds:searchStream";
        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        req.Headers.Add("developer-token", creds.DeveloperToken);
        req.Headers.Add("login-customer-id", customerId);
        req.Content = new StringContent(JsonSerializer.Serialize(new { query = gaql }), Encoding.UTF8, "application/json");

        using var resp = await _http.SendAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
        {
            _logger.LogWarning("Google Ads API {Status}: {Body}", (int)resp.StatusCode, Truncate(body));
            return Array.Empty<CampaignSpendRow>();
        }

        var rows = new List<CampaignSpendRow>();
        using var doc = JsonDocument.Parse(body);
        // searchStream devolve um ARRAY de batches, cada um com "results".
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return rows;

        foreach (var batch in doc.RootElement.EnumerateArray())
        {
            if (!batch.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array) continue;
            foreach (var r in results.EnumerateArray())
            {
                var campaign = r.TryGetProperty("campaign", out var cp) ? cp : default;
                var metrics = r.TryGetProperty("metrics", out var mt) ? mt : default;
                var segments = r.TryGetProperty("segments", out var sg) ? sg : default;

                var cid = campaign.ValueKind == JsonValueKind.Object && campaign.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? "" : "";
                var cname = campaign.ValueKind == JsonValueKind.Object && campaign.TryGetProperty("name", out var nmEl) ? nmEl.GetString() ?? "" : "";
                var costStr = metrics.ValueKind == JsonValueKind.Object && metrics.TryGetProperty("costMicros", out var cmEl) ? cmEl.GetString() : null;
                var dateStr = segments.ValueKind == JsonValueKind.Object && segments.TryGetProperty("date", out var dEl) ? dEl.GetString() : null;

                if (cid.Length == 0 || costStr is null || dateStr is null) continue;
                if (!long.TryParse(costStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var micros)) continue;
                if (!DateOnly.TryParse(dateStr, CultureInfo.InvariantCulture, out var date)) continue;
                rows.Add(new CampaignSpendRow(cid, cname, date, micros / 1_000_000m, "BRL"));
            }
        }

        return rows;
    }

    private async Task<string?> RefreshAccessTokenAsync(AdsCredentials creds, string refreshToken, CancellationToken ct)
    {
        try
        {
            using var doc = await PostFormAsync(TokenUrl, new Dictionary<string, string>
            {
                ["client_id"] = creds.ClientId!,
                ["client_secret"] = creds.ClientSecret!,
                ["refresh_token"] = refreshToken,
                ["grant_type"] = "refresh_token",
            }, null, ct);
            return GetStr(doc, "access_token");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Falha ao renovar access_token do Google.");
            return null;
        }
    }

    private async Task<string> FirstAccessibleCustomerAsync(AdsCredentials creds, string accessToken, CancellationToken ct)
    {
        var url = $"{AdsBase}/customers:listAccessibleCustomers";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        req.Headers.Add("developer-token", creds.DeveloperToken);
        using var resp = await _http.SendAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"listAccessibleCustomers {(int)resp.StatusCode}: {Truncate(body)}");

        using var doc = JsonDocument.Parse(body);
        if (doc.RootElement.TryGetProperty("resourceNames", out var names) && names.ValueKind == JsonValueKind.Array && names.GetArrayLength() > 0)
        {
            var rn = names[0].GetString() ?? ""; // "customers/1234567890"
            var idx = rn.LastIndexOf('/');
            return idx >= 0 ? rn[(idx + 1)..] : rn;
        }
        throw new InvalidOperationException("Nenhum customer acessível no Google Ads.");
    }

    private async Task<JsonDocument> PostFormAsync(string url, Dictionary<string, string> form, string? bearer, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = new FormUrlEncodedContent(form) };
        if (bearer is not null) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        using var resp = await _http.SendAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
        {
            _logger.LogWarning("Google OAuth {Status}: {Body}", (int)resp.StatusCode, Truncate(body));
            throw new InvalidOperationException($"Google OAuth retornou {(int)resp.StatusCode}.");
        }
        return JsonDocument.Parse(body);
    }

    private static string? GetStr(JsonDocument doc, string prop) =>
        doc.RootElement.TryGetProperty(prop, out var el) ? el.GetString() : null;

    private static string Truncate(string s) => s.Length <= 500 ? s : s[..500];
}
