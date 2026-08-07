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
            // O cache só entra quando a Meta não deu nada: a miniatura guardada é de 64px e,
            // esticada na linha, é exatamente o borrão que se quer evitar.
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

        // ── A imagem vem de OUTRA rota ──────────────────────────────────────
        // /insights devolve número, não criativo — pedir "creative" ali é ignorado em
        // silêncio, e foi por isso que o card saiu sem foto. A miniatura está em /ads.
        // Uma chamada só para a conta inteira, casada por id depois.
        await PreencherImagensAsync(http, contaId, token, saida, ct);

        return [.. saida.OrderByDescending(x => x.Gasto)];
    }

    /// <summary>
    /// Miniatura de cada anúncio, de /ads. Falhar aqui não é motivo para perder as métricas:
    /// o card sem foto ainda decide se o anúncio fica ou sai.
    /// </summary>
    private async Task PreencherImagensAsync(
        HttpClient http, string contaId, string token,
        List<AnuncioLinhaDto> linhas, CancellationToken ct)
    {
        if (linhas.Count == 0) return;

        try
        {
            // limit=100, não 250: com 250 a Meta responde "Please reduce the amount of data
            // you're asking for" e devolve ZERO anúncio. O erro é HTTP 200 com corpo de erro,
            // então passava despercebido e a tela caía na miniatura velha de 64px — o "ainda
            // embaçado" que ninguém conseguia explicar.
            var url = $"{GraphBase}/act_{contaId}/ads"
                    + "?limit=100&fields="
                    + Uri.EscapeDataString("id,name,creative{thumbnail_url,image_url,video_id,object_type}")
                    + $"&access_token={Uri.EscapeDataString(token)}";

            using var resp = await http.GetAsync(url, ct);
            if (!resp.IsSuccessStatusCode)
            {
                logger.LogWarning("Listagem de anúncios falhou: HTTP {Status}", (int)resp.StatusCode);
                return;
            }

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));

            // A Meta devolve erro DENTRO de um 200. Sem olhar aqui, a falha vira silêncio e a
            // tela mostra imagem velha sem ninguém saber por quê.
            if (doc.RootElement.TryGetProperty("error", out var erro))
            {
                logger.LogWarning("Listagem de anúncios recusada pela Meta: {Msg}",
                    erro.TryGetProperty("message", out var m) ? m.GetString() : "sem mensagem");
                return;
            }

            if (!doc.RootElement.TryGetProperty("data", out var data)) return;

            var porId = new Dictionary<string, (string? img, string? nome)>();
            var videos = new Dictionary<string, string>();  // adId -> videoId

            foreach (var a in data.EnumerateArray())
            {
                var id = Txt(a, "id");
                if (string.IsNullOrWhiteSpace(id)) continue;

                string? img = null;
                if (a.TryGetProperty("creative", out var cr) && cr.ValueKind == JsonValueKind.Object)
                {
                    // image_url é a versão grande do criativo de imagem; thumbnail_url é
                    // sempre 64px. Anúncio de vídeo não tem nenhuma das duas em tamanho útil —
                    // o quadro em alta vem do próprio vídeo, logo abaixo.
                    img = Txt(cr, "image_url") ?? Txt(cr, "thumbnail_url");
                    var vid = Txt(cr, "video_id");
                    if (!string.IsNullOrWhiteSpace(vid)) videos[id!] = vid!;
                }

                porId[id!] = (img, Txt(a, "name"));
            }

            // Quadro em alta dos anúncios de vídeo. Exige pages_read_engagement: sem ela a
            // Meta responde "Missing permissions" e ficamos com os 64px, que na tela viravam
            // borrão. Com ela, o mesmo anúncio entrega 720x1280.
            foreach (var (adId, videoId) in videos)
            {
                var quadro = await QuadroDoVideoAsync(http, videoId, token, ct);
                if (string.IsNullOrWhiteSpace(quadro)) continue;

                var atual = porId[adId];
                porId[adId] = (quadro, atual.nome);
            }

            foreach (var l in linhas)
            {
                if (!porId.TryGetValue(l.AnuncioId, out var info)) continue;
                l.Imagem ??= info.img;
                if (string.IsNullOrWhiteSpace(l.Nome)) l.Nome = info.nome;
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Miniaturas de anúncio indisponíveis");
        }
    }

    /// <summary>
    /// O quadro do vídeo, preferindo o que a Meta marca como preferido e, na falta dele, o
    /// maior. Silencioso de propósito: sem permissão de Página o card volta ao thumbnail.
    /// </summary>
    private static async Task<string?> QuadroDoVideoAsync(
        HttpClient http, string videoId, string token, CancellationToken ct)
    {
        try
        {
            var url = $"{GraphBase}/{videoId}"
                    + "?fields=" + Uri.EscapeDataString("thumbnails{uri,width,height,is_preferred}")
                    + $"&access_token={Uri.EscapeDataString(token)}";

            using var resp = await http.GetAsync(url, ct);
            if (!resp.IsSuccessStatusCode) return null;

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            if (!doc.RootElement.TryGetProperty("thumbnails", out var th)
                || !th.TryGetProperty("data", out var itens)
                || itens.ValueKind != JsonValueKind.Array) return null;

            string? melhor = null;
            long melhorArea = 0;
            foreach (var t in itens.EnumerateArray())
            {
                var uri = Txt(t, "uri");
                if (string.IsNullOrWhiteSpace(uri)) continue;

                if (t.TryGetProperty("is_preferred", out var pref)
                    && pref.ValueKind == JsonValueKind.True) return uri;

                var area = Num(t, "width") * Num(t, "height");
                if (area > melhorArea) { melhorArea = area; melhor = uri; }
            }
            return melhor;
        }
        catch { return null; }
    }

    private static long Num(JsonElement e, string p) =>
        e.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt64() : 0;

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
