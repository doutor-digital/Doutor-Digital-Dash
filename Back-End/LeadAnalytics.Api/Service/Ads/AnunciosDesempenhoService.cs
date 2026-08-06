using System.Text.Json;
using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.DTOs.Saude;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace LeadAnalytics.Api.Service.Ads;

/// <summary>
/// Desempenho de cada anúncio no período: gasto, alcance, cliques, CTR, CPC, conversas e o
/// custo por conversa — com a foto do criativo.
///
/// POR QUE VEM DA META NA HORA, E NÃO DO NOSSO BANCO
/// -------------------------------------------------
/// O que gravamos é gasto por CAMPANHA e por dia, que é o suficiente para o custo por lead.
/// Estas métricas são por ANÚNCIO e mudam pouco depois de fechado o dia; guardar tudo exigiria
/// mais uma tabela e mais um fluxo para uma tela que se abre algumas vezes ao dia.
///
/// Em compensação: cache de 10 minutos, e falha da Meta devolve lista vazia em vez de derrubar
/// o relatório inteiro. Nunca é o caminho de nenhum número do dashboard — é leitura de tela.
///
/// CUSTO POR CONVERSA, NÃO CUSTO POR LEAD
/// --------------------------------------
/// A Meta conta "conversas de WhatsApp iniciadas". Nem toda conversa vira lead na Kommo, então
/// chamar isso de custo por lead inflaria o resultado do anúncio. O custo por lead de verdade
/// sai do cruzamento com o CRM, e vive no bloco de campanhas.
/// </summary>
public class AnunciosDesempenhoService(
    AppDbContext db,
    ProtectedTokenService tokens,
    IHttpClientFactory httpFactory,
    IMemoryCache cache,
    ILogger<AnunciosDesempenhoService> logger)
{
    private const string GraphBase = "https://graph.facebook.com/v23.0";

    /// <summary>Janela do cache. A tela é consultada aos punhados; a Meta cobra por chamada.</summary>
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(10);

    public async Task<List<AnuncioLinhaDto>> GetAsync(
        int tenantId, int? unitId, DateOnly de, DateOnly ate, CancellationToken ct = default)
    {
        var chave = $"ads-desemp:{tenantId}:{unitId}:{de:yyyy-MM-dd}:{ate:yyyy-MM-dd}";
        if (cache.TryGetValue(chave, out List<AnuncioLinhaDto>? guardado) && guardado is not null)
            return guardado;

        var conta = await db.AdAccounts.AsNoTracking()
            .Where(a => a.ClinicId == tenantId && a.Provider == "meta"
                        && (a.UnitId == unitId || a.UnitId == null))
            .OrderByDescending(a => a.UnitId == unitId)
            .FirstOrDefaultAsync(ct);

        if (conta?.AccessTokenEnc is null || string.IsNullOrWhiteSpace(conta.ExternalAccountId))
            return [];

        var token = tokens.Unprotect(conta.AccessTokenEnc);
        if (string.IsNullOrWhiteSpace(token) || token.StartsWith("stub-", StringComparison.Ordinal))
            return [];

        List<AnuncioLinhaDto> linhas;
        try
        {
            linhas = await BuscarAsync(conta.ExternalAccountId!, token!, de, ate, ct);
        }
        catch (Exception ex)
        {
            // Meta fora do ar não derruba o relatório: o bloco some e o resto continua.
            logger.LogWarning(ex, "Desempenho de anúncios indisponível para a clínica {Clinica}", tenantId);
            return [];
        }

        // Completa nome e foto pelo nosso cache quando a Meta não devolveu o criativo.
        var ids = linhas.Select(l => l.AnuncioId).ToList();
        var criativos = await db.AdCreatives.AsNoTracking()
            .Where(c => ids.Contains(c.AdId))
            .ToListAsync(ct);

        foreach (var l in linhas)
        {
            var c = criativos.FirstOrDefault(x => x.AdId == l.AnuncioId);
            l.Imagem ??= c?.ThumbnailUrl;
            if (string.IsNullOrWhiteSpace(l.Nome)) l.Nome = c?.Name ?? l.AnuncioId;
        }

        cache.Set(chave, linhas, Ttl);
        return linhas;
    }

    private async Task<List<AnuncioLinhaDto>> BuscarAsync(
        string contaId, string token, DateOnly de, DateOnly ate, CancellationToken ct)
    {
        var http = httpFactory.CreateClient();
        http.Timeout = TimeSpan.FromSeconds(20);

        var campos = "ad_id,ad_name,campaign_name,spend,impressions,reach,clicks,ctr,cpc,actions";
        var janela = $"{{\"since\":\"{de:yyyy-MM-dd}\",\"until\":\"{ate:yyyy-MM-dd}\"}}";
        var url = $"{GraphBase}/act_{contaId}/insights"
                + $"?level=ad&limit=50&fields={Uri.EscapeDataString(campos)}"
                + $"&time_range={Uri.EscapeDataString(janela)}"
                + $"&access_token={Uri.EscapeDataString(token)}";

        using var resp = await http.GetAsync(url, ct);
        if (!resp.IsSuccessStatusCode) return [];

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        if (!doc.RootElement.TryGetProperty("data", out var data)) return [];

        var saida = new List<AnuncioLinhaDto>();
        foreach (var r in data.EnumerateArray())
        {
            var gasto = Dec(r, "spend");
            var conversas = Conversas(r);

            saida.Add(new AnuncioLinhaDto
            {
                AnuncioId = Txt(r, "ad_id") ?? "",
                Nome = Txt(r, "ad_name"),
                Campanha = Txt(r, "campaign_name"),
                Gasto = gasto,
                Alcance = Lng(r, "reach"),
                Impressoes = Lng(r, "impressions"),
                Cliques = Lng(r, "clicks"),
                Ctr = Dec(r, "ctr"),
                Cpc = Dec(r, "cpc"),
                Conversas = conversas,
                // Sem conversa não existe custo por conversa. Zero ali seria lido como
                // "de graça", que é o oposto de "não converteu nada".
                CustoPorConversa = conversas > 0 ? Math.Round(gasto / conversas, 2) : null,
            });
        }

        return [.. saida.OrderByDescending(x => x.Gasto)];
    }

    /// <summary>Conversas de WhatsApp iniciadas — o desfecho que estes anúncios perseguem.</summary>
    private static int Conversas(JsonElement r)
    {
        if (!r.TryGetProperty("actions", out var acoes) || acoes.ValueKind != JsonValueKind.Array)
            return 0;

        var total = 0;
        foreach (var a in acoes.EnumerateArray())
        {
            var tipo = a.TryGetProperty("action_type", out var t) ? t.GetString() ?? "" : "";
            if (!tipo.Contains("messaging_conversation_started", StringComparison.OrdinalIgnoreCase))
                continue;
            if (a.TryGetProperty("value", out var v)
                && decimal.TryParse(v.GetString(), System.Globalization.NumberStyles.Any,
                                    System.Globalization.CultureInfo.InvariantCulture, out var n))
                total += (int)n;
        }
        return total;
    }

    private static string? Txt(JsonElement e, string p) =>
        e.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static decimal Dec(JsonElement e, string p) =>
        e.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.String
        && decimal.TryParse(v.GetString(), System.Globalization.NumberStyles.Any,
                            System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : 0;

    private static long Lng(JsonElement e, string p) =>
        e.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.String
        && long.TryParse(v.GetString(), out var n) ? n : 0;
}
