using System.Text.Json;
using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.DTOs.Response;
using LeadAnalytics.Api.Models;
using LeadAnalytics.Api.Service;
using Microsoft.EntityFrameworkCore;

namespace LeadAnalytics.Api.Service.Ads;

/// <summary>
/// Põe cara nos anúncios do dashboard: resolve id → nome + miniatura na Graph API e
/// guarda o resultado.
///
/// Só funciona quando a clínica conectou o Meta Ads na Central de Integrações — é de
/// lá que sai o token. Sem conexão, sem token válido ou com erro na Meta, o card
/// continua funcionando com o id; miniatura é enfeite útil, não requisito.
///
/// A busca acontece no caminho do dashboard, então é limitada: só os anúncios que a
/// tela vai mostrar, um por vez, com timeout curto. O cache (<see cref="AdCreative"/>)
/// faz o custo cair a zero a partir do segundo carregamento.
/// </summary>
public class AdCreativeService(
    AppDbContext db,
    IHttpClientFactory httpFactory,
    ProtectedTokenService tokens,
    ILogger<AdCreativeService> logger)
{
    private const string GraphBase = "https://graph.facebook.com/v19.0";

    /// <summary>Idade a partir da qual a miniatura é rebuscada (a URL da Meta é assinada e expira).</summary>
    private static readonly TimeSpan Validade = TimeSpan.FromDays(5);

    /// <summary>Teto de buscas novas por carregamento — o resto fica para a próxima.</summary>
    private const int MaxBuscasPorRequisicao = 10;

    /// <summary>
    /// Preenche nome e miniatura nas linhas do card. Nunca lança: qualquer falha deixa
    /// as linhas como estavam.
    /// </summary>
    public async Task EnriquecerAsync(List<AnuncioDesempenhoDto> linhas, int clinicId, int? unitId, CancellationToken ct)
    {
        if (linhas.Count == 0) return;

        try
        {
            // Só ids da Meta (dígitos). Título já resolvido pelo rastreio não vira busca.
            var ids = linhas.Select(l => l.Anuncio)
                            .Where(a => a.Length >= 8 && a.All(char.IsDigit))
                            .Distinct()
                            .ToList();
            if (ids.Count == 0) return;

            var cache = await db.Set<AdCreative>().AsNoTracking()
                .Where(c => ids.Contains(c.AdId))
                .ToDictionaryAsync(c => c.AdId, ct);

            var agora = DateTime.UtcNow;
            var faltando = ids.Where(id => !cache.TryGetValue(id, out var c) || agora - c.FetchedAt > Validade)
                              .Take(MaxBuscasPorRequisicao)
                              .ToList();

            if (faltando.Count > 0)
            {
                var token = await ObterTokenAsync(clinicId, unitId, ct);
                if (!string.IsNullOrWhiteSpace(token))
                {
                    foreach (var id in faltando)
                    {
                        var novo = await BuscarNaMetaAsync(id, token!, ct);
                        if (novo is not null) cache[id] = novo;
                    }
                    await db.SaveChangesAsync(ct);
                }
            }

            foreach (var linha in linhas)
            {
                if (!cache.TryGetValue(linha.Anuncio, out var c) || c.NotFound) continue;
                linha.Thumbnail = c.ThumbnailUrl;
                linha.Permalink = c.PermalinkUrl;
                if (!string.IsNullOrWhiteSpace(c.Name)) linha.Nome = c.Name;
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Falha ao enriquecer anúncios com miniatura da Meta");
        }
    }

    /// <summary>Token da conta de anúncios conectada — prefere a da unidade, cai para a da clínica.</summary>
    private async Task<string?> ObterTokenAsync(int clinicId, int? unitId, CancellationToken ct)
    {
        var contas = await db.AdAccounts.AsNoTracking()
            .Where(a => a.Provider == "meta" && a.Status == "connected" && a.ClinicId == clinicId)
            .ToListAsync(ct);

        var conta = contas.FirstOrDefault(a => unitId.HasValue && a.UnitId == unitId)
                 ?? contas.FirstOrDefault(a => a.UnitId == null)
                 ?? contas.FirstOrDefault();
        if (conta?.AccessTokenEnc is null) return null;

        var token = tokens.Unprotect(conta.AccessTokenEnc);
        // A conexão em modo demo grava um token de mentira; não adianta chamar a Meta com ele.
        return string.IsNullOrWhiteSpace(token) || token.StartsWith("stub-", StringComparison.Ordinal) ? null : token;
    }

    /// <summary>
    /// Busca o criativo. Tenta como anúncio; se a Meta não reconhecer, tenta como
    /// publicação — o rastreio de alcance orgânico grava id de post, não de anúncio.
    /// </summary>
    private async Task<AdCreative?> BuscarNaMetaAsync(string id, string token, CancellationToken ct)
    {
        var reg = await db.Set<AdCreative>().FirstOrDefaultAsync(c => c.AdId == id, ct);
        if (reg is null)
        {
            reg = new AdCreative { AdId = id };
            db.Set<AdCreative>().Add(reg);
        }
        reg.FetchedAt = DateTime.UtcNow;
        reg.NotFound = true;

        var http = httpFactory.CreateClient();
        http.Timeout = TimeSpan.FromSeconds(6);

        var comoAnuncio = $"{GraphBase}/{id}?fields=name,creative%7Bthumbnail_url,object_story_id%7D"
                        + $"&thumbnail_width=240&thumbnail_height=240&access_token={Uri.EscapeDataString(token)}";
        var doc = await LerJsonAsync(http, comoAnuncio, ct);
        if (doc is not null && doc.RootElement.TryGetProperty("creative", out var criativo))
        {
            reg.NotFound = false;
            reg.Name = Texto(doc.RootElement, "name");
            reg.ThumbnailUrl = Texto(criativo, "thumbnail_url");
            var storyId = Texto(criativo, "object_story_id");
            if (!string.IsNullOrEmpty(storyId))
                reg.PermalinkUrl = $"https://www.facebook.com/{storyId}";
            doc.Dispose();
            return reg;
        }
        doc?.Dispose();

        var comoPost = $"{GraphBase}/{id}?fields=full_picture,permalink_url,message"
                     + $"&access_token={Uri.EscapeDataString(token)}";
        doc = await LerJsonAsync(http, comoPost, ct);
        if (doc is not null && doc.RootElement.TryGetProperty("full_picture", out _))
        {
            reg.NotFound = false;
            reg.ThumbnailUrl = Texto(doc.RootElement, "full_picture");
            reg.PermalinkUrl = Texto(doc.RootElement, "permalink_url");
            var msg = Texto(doc.RootElement, "message");
            if (!string.IsNullOrWhiteSpace(msg))
                reg.Name = msg.Length > 60 ? msg[..60].TrimEnd() + "…" : msg;
        }
        doc?.Dispose();

        return reg;
    }

    private async Task<JsonDocument?> LerJsonAsync(HttpClient http, string url, CancellationToken ct)
    {
        try
        {
            using var resp = await http.GetAsync(url, ct);
            if (!resp.IsSuccessStatusCode) return null;
            return await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Graph API não respondeu para {Url}", url.Split("&access_token")[0]);
            return null;
        }
    }

    private static string? Texto(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
