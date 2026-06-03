using System.Globalization;
using System.Text.Json;
using LeadAnalytics.Api.Models;
using LeadAnalytics.Api.Service;

namespace LeadAnalytics.Api.Service.Ads;

/// <summary>
/// Provedor do Meta Ads. Sem credenciais (<see cref="AdsCredentials.IsConfigured"/> = false) opera em
/// STUB (mock). Com credenciais, usa a Graph API real: troca o code por um token de longa duração,
/// descobre a ad account e puxa o gasto por campanha/dia via Insights.
/// </summary>
public class MetaAdsProvider(HttpClient http, ProtectedTokenService tokens, ILogger<MetaAdsProvider> logger) : IAdsProvider
{
    private const string GraphBase = "https://graph.facebook.com/v19.0";
    private readonly HttpClient _http = http;
    private readonly ProtectedTokenService _tokens = tokens;
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
        if (!creds.IsConfigured || code.StartsWith("STUB", StringComparison.Ordinal))
        {
            return new AdsTokenResult("act_DEMO_META", "Conta Meta Ads (demo)", "stub-meta-access-token", null, DateTime.UtcNow.AddDays(60));
        }

        // 1) code → access_token de curta duração.
        var shortUrl = $"{GraphBase}/oauth/access_token"
            + $"?client_id={Uri.EscapeDataString(creds.ClientId!)}"
            + $"&redirect_uri={Uri.EscapeDataString(redirectUri)}"
            + $"&client_secret={Uri.EscapeDataString(creds.ClientSecret!)}"
            + $"&code={Uri.EscapeDataString(code)}";
        using var shortDoc = await GetJsonAsync(shortUrl, ct);
        var shortToken = shortDoc.RootElement.TryGetProperty("access_token", out var st) ? st.GetString() : null;
        if (string.IsNullOrEmpty(shortToken))
            throw new InvalidOperationException("Meta não retornou access_token na troca do code.");

        // 2) troca por token de LONGA duração (~60 dias).
        var longUrl = $"{GraphBase}/oauth/access_token"
            + "?grant_type=fb_exchange_token"
            + $"&client_id={Uri.EscapeDataString(creds.ClientId!)}"
            + $"&client_secret={Uri.EscapeDataString(creds.ClientSecret!)}"
            + $"&fb_exchange_token={Uri.EscapeDataString(shortToken)}";
        using var longDoc = await GetJsonAsync(longUrl, ct);
        var accessToken = longDoc.RootElement.TryGetProperty("access_token", out var lt) ? lt.GetString() : shortToken;
        var expiresIn = longDoc.RootElement.TryGetProperty("expires_in", out var ei) && ei.TryGetInt64(out var s) ? s : 60 * 24 * 3600;

        // 3) descobre a primeira ad account do usuário.
        var acctUrl = $"{GraphBase}/me/adaccounts?fields=account_id,name&access_token={Uri.EscapeDataString(accessToken!)}";
        using var acctDoc = await GetJsonAsync(acctUrl, ct);
        string externalId = "act_unknown", name = "Conta Meta Ads";
        if (acctDoc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array && data.GetArrayLength() > 0)
        {
            var first = data[0];
            var accountId = first.TryGetProperty("account_id", out var aid) ? aid.GetString() : null;
            externalId = string.IsNullOrEmpty(accountId) ? externalId : $"act_{accountId}";
            name = first.TryGetProperty("name", out var nm) ? nm.GetString() ?? name : name;
        }

        return new AdsTokenResult(externalId, name, accessToken!, null, DateTime.UtcNow.AddSeconds(expiresIn));
    }

    public async Task<IReadOnlyList<CampaignSpendRow>> FetchDailySpendAsync(
        AdsCredentials creds, AdAccount account, DateOnly from, DateOnly to, CancellationToken ct)
    {
        if (!creds.IsConfigured)
            return StubSpend.Generate("meta", from, to);

        var token = _tokens.Unprotect(account.AccessTokenEnc);
        if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(account.ExternalAccountId))
        {
            _logger.LogWarning("Conta Meta {Id} sem token/act_id — pulando.", account.Id);
            return Array.Empty<CampaignSpendRow>();
        }

        var since = from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var until = to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var timeRange = Uri.EscapeDataString($"{{\"since\":\"{since}\",\"until\":\"{until}\"}}");
        var url = $"{GraphBase}/{account.ExternalAccountId}/insights"
            + "?level=campaign&time_increment=1"
            + "&fields=campaign_id,campaign_name,spend"
            + $"&time_range={timeRange}&limit=500&access_token={Uri.EscapeDataString(token)}";

        var rows = new List<CampaignSpendRow>();
        var page = 0;
        while (!string.IsNullOrEmpty(url) && page++ < 50) // teto de páginas
        {
            using var doc = await GetJsonAsync(url, ct);
            if (doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in data.EnumerateArray())
                {
                    var cid = el.TryGetProperty("campaign_id", out var c) ? c.GetString() ?? "" : "";
                    var cname = el.TryGetProperty("campaign_name", out var cn) ? cn.GetString() ?? "" : "";
                    var spendStr = el.TryGetProperty("spend", out var sp) ? sp.GetString() : null;
                    var dateStr = el.TryGetProperty("date_start", out var ds) ? ds.GetString() : null;
                    if (cid.Length == 0 || spendStr is null || dateStr is null) continue;
                    if (!decimal.TryParse(spendStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var spend)) continue;
                    if (!DateOnly.TryParse(dateStr, CultureInfo.InvariantCulture, out var date)) continue;
                    rows.Add(new CampaignSpendRow(cid, cname, date, spend, "BRL"));
                }
            }
            url = doc.RootElement.TryGetProperty("paging", out var paging)
                  && paging.TryGetProperty("next", out var next) ? next.GetString() : null;
        }

        return rows;
    }

    private async Task<JsonDocument> GetJsonAsync(string url, CancellationToken ct)
    {
        using var resp = await _http.GetAsync(url, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
        {
            _logger.LogWarning("Meta API {Status}: {Body}", (int)resp.StatusCode, Truncate(body));
            throw new InvalidOperationException($"Meta API retornou {(int)resp.StatusCode}.");
        }
        return JsonDocument.Parse(body);
    }

    private static string Truncate(string s) => s.Length <= 500 ? s : s[..500];
}
