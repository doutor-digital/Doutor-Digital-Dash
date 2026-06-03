using LeadAnalytics.Api.Models;

namespace LeadAnalytics.Api.Service.Ads;

/// <summary>Resultado da troca do código OAuth: identifica a conta e traz os tokens.</summary>
public record AdsTokenResult(
    string ExternalAccountId,
    string AccountName,
    string AccessToken,
    string? RefreshToken,
    DateTime? ExpiresAt);

/// <summary>Gasto de uma campanha num dia (linha bruta vinda do provedor).</summary>
public record CampaignSpendRow(
    string CampaignId,
    string CampaignName,
    DateOnly Date,
    decimal Spend,
    string Currency);

/// <summary>
/// Abstração de um provedor de anúncios (Meta/Google). Hoje as implementações são STUB
/// (devolvem mock) — quando houver credenciais reais (seções <c>Ads:Meta</c>/<c>Ads:Google</c>
/// no appsettings), <see cref="IsLive"/> vira true e os métodos passam a bater na API real.
/// </summary>
public interface IAdsProvider
{
    /// <summary><c>meta</c> | <c>google</c>.</summary>
    string Provider { get; }

    /// <summary>URL para onde o usuário é mandado pra autorizar (OAuth). No stub, volta direto pro callback.</summary>
    string GetAuthUrl(AdsCredentials creds, string state, string redirectUri);

    /// <summary>Troca o código do callback por tokens + dados da conta.</summary>
    Task<AdsTokenResult> ExchangeCodeAsync(AdsCredentials creds, string code, string redirectUri, CancellationToken ct);

    /// <summary>Puxa o gasto por campanha/dia no intervalo.</summary>
    Task<IReadOnlyList<CampaignSpendRow>> FetchDailySpendAsync(
        AdsCredentials creds, AdAccount account, DateOnly from, DateOnly to, CancellationToken ct);
}

/// <summary>Gerador de gasto MOCK determinístico — usado enquanto não há credenciais reais.</summary>
internal static class StubSpend
{
    private static int StableHash(string s)
    {
        // hash determinístico (o String.GetHashCode do .NET é randomizado por processo).
        var h = 17;
        foreach (var c in s) h = unchecked(h * 31 + c);
        return h & 0x7fffffff;
    }

    public static IReadOnlyList<CampaignSpendRow> Generate(string provider, DateOnly from, DateOnly to)
    {
        var campaigns = provider == "google"
            ? new[] { ("g-101", "Google · Search Marca"), ("g-102", "Google · Display Remarketing") }
            : new[] { ("m-23851", "Meta · Implante - Conversão"), ("m-23852", "Meta · Lookalike 1%") };

        var rows = new List<CampaignSpendRow>();
        for (var d = from; d <= to; d = d.AddDays(1))
        {
            foreach (var (cid, cname) in campaigns)
            {
                var seed = StableHash(cid + d.DayNumber);
                decimal spend = 80 + (seed % 320); // R$ 80–400/dia
                rows.Add(new CampaignSpendRow(cid, cname, d, spend, "BRL"));
            }
        }
        return rows;
    }
}
