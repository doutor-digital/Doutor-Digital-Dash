using System.Text.Json;
using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.DTOs.Saude;
using Microsoft.EntityFrameworkCore;

namespace LeadAnalytics.Api.Service.Ads;

/// <summary>
/// Quanto do que veio de anúncio o rastreio conseguiu identificar, unidade por unidade.
///
/// POR QUE ISTO EXISTE
/// -------------------
/// O rastreio de clique (n8n → campos "⌂" do lead) é o que alimenta a tela de Mídia e o custo
/// por conversa. Quando ele para numa unidade, nada quebra na tela: a Mídia simplesmente mostra
/// menos anúncios, e ninguém percebe. Em 01/09/2026 a Serra estava com 7% de cobertura havia
/// semanas — o rastreio dela estava ligado, respondendo, e mesmo assim identificava quase nada.
/// Não havia número nenhum na casa que denunciasse isso.
///
/// O DENOMINADOR É QUEM VEIO DE ANÚNCIO, NÃO O TOTAL DE LEADS
/// ----------------------------------------------------------
/// Dividir pelo total de leads mistura indicação, fachada e site na conta e faz toda unidade
/// parecer ruim. Pior: não separa "o rastreio quebrou" de "esta unidade não está anunciando" —
/// os dois dariam 0%, e o segundo não é problema nenhum.
///
/// Por isso o denominador é o campo de origem do próprio cartão: lead marcado como Meta-* ou
/// "WhatsApp anúncio" é lead que DEVERIA ter vindo com anúncio identificado. Unidade sem nenhum
/// lead de anúncio no período fica com a cobertura em branco — em branco é honesto, 0% mentiria.
///
/// 100% NÃO É O ALVO
/// -----------------
/// Nem todo lead de anúncio chega pelo botão de WhatsApp do anúncio: quem vê o anúncio e depois
/// procura a clínica pela bio, pela DM ou pela busca entra marcado como Meta-* e sem referral
/// nenhum para a Meta nos mandar. Imperatriz, que é a mais antiga e a mais bem configurada,
/// opera perto de 79% — é esse o teto prático, e é ele que ancora as faixas abaixo.
/// </summary>
public class RastreioCoberturaService(AppDbContext db)
{
    private readonly AppDbContext _db = db;

    /// <summary>Campo "⌂ ID do anúncio" — a marca de que o rastreio pegou este lead.</summary>
    private const string NomeCampoAnuncio = "id do anúncio";

    /// <summary>Piso da faixa verde. Ancorado na Imperatriz — ver o resumo da classe.</summary>
    private const int PisoOk = 60;

    /// <summary>Abaixo disto não é "cobertura parcial", é rastreio com problema.</summary>
    private const int PisoParcial = 25;

    public async Task<List<RastreioCoberturaDto>> GetAsync(
        int? tenantId, DateTime de, DateTime ate, CancellationToken ct = default)
    {
        var leads = await _db.Leads.AsNoTracking().ExcludeDeleted()
            .Where(l => (!tenantId.HasValue || l.TenantId == tenantId.Value)
                        && l.UnitId != null
                        && (l.OriginalCreatedAt ?? l.CreatedAt) >= de
                        && (l.OriginalCreatedAt ?? l.CreatedAt) <= ate
                        && l.CustomFieldsJson != null)
            .Select(l => new
            {
                l.UnitId,
                Unidade = l.Unit!.Name,
                l.CustomFieldsJson,
                Criado = l.OriginalCreatedAt ?? l.CreatedAt,
            })
            .ToListAsync(ct);

        var porUnidade = new Dictionary<int, RastreioCoberturaDto>();

        foreach (var l in leads)
        {
            var uid = l.UnitId!.Value;
            if (!porUnidade.TryGetValue(uid, out var dto))
            {
                dto = new RastreioCoberturaDto { UnidadeId = uid, Unidade = l.Unidade };
                porUnidade[uid] = dto;
            }

            dto.Leads++;

            var (rastreado, deAnuncio) = Ler(l.CustomFieldsJson);
            if (deAnuncio) dto.DeAnuncio++;
            if (rastreado)
            {
                dto.Rastreados++;
                if (dto.UltimoRastreado is null || l.Criado > dto.UltimoRastreado)
                    dto.UltimoRastreado = l.Criado;
            }
        }

        foreach (var d in porUnidade.Values) Classificar(d);

        // Quem tem mais lead de anúncio em jogo aparece primeiro: é onde o rastreio quebrado
        // custa mais caro. Empate desce para o total de leads.
        return [.. porUnidade.Values
            .OrderByDescending(d => d.DeAnuncio)
            .ThenByDescending(d => d.Leads)];
    }

    private static void Classificar(RastreioCoberturaDto d)
    {
        if (d.DeAnuncio == 0)
        {
            d.Status = "sem_anuncio";
            d.Detalhe = "Nenhum lead de anúncio no período — não há o que rastrear aqui.";
            return;
        }

        d.CoberturaPct = (int)Math.Round(100.0 * d.Rastreados / d.DeAnuncio);

        if (d.Rastreados == 0)
        {
            d.Status = "sem_rastreio";
            d.Detalhe = $"{d.DeAnuncio} leads vieram de anúncio e nenhum foi identificado. "
                      + "Ou o rastreio não está ligado nesta unidade, ou acabou de ser ligado.";
            return;
        }

        if (d.CoberturaPct < PisoParcial)
        {
            d.Status = "falha";
            d.Detalhe = $"Só {d.Rastreados} de {d.DeAnuncio} leads de anúncio foram identificados. "
                      + "O rastreio responde, mas está deixando quase tudo passar.";
            return;
        }

        if (d.CoberturaPct < PisoOk)
        {
            d.Status = "parcial";
            d.Detalhe = $"{d.Rastreados} de {d.DeAnuncio} leads de anúncio identificados.";
            return;
        }

        d.Status = "ok";
        d.Detalhe = $"{d.Rastreados} de {d.DeAnuncio} leads de anúncio identificados.";
    }

    /// <summary>
    /// Uma passada só no cartão: se tem anúncio identificado e se a origem diz que veio de mídia
    /// paga. Cartão torto não derruba a página — ele só não conta.
    /// </summary>
    private static (bool Rastreado, bool DeAnuncio) Ler(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return (false, false);

        var rastreado = false;
        var deAnuncio = false;

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return (false, false);

            foreach (var f in doc.RootElement.EnumerateArray())
            {
                if (!f.TryGetProperty("field_name", out var n) || n.ValueKind != JsonValueKind.String) continue;
                if (!f.TryGetProperty("value", out var v)) continue;

                var nome = (n.GetString() ?? "").ToLowerInvariant();
                var valor = v.ValueKind == JsonValueKind.String ? v.GetString() : v.ToString();
                if (string.IsNullOrWhiteSpace(valor)) continue;

                if (!rastreado && nome.Contains(NomeCampoAnuncio)) rastreado = true;

                if (!deAnuncio && OrigemDoLead.EhCampoOrigem(nome) && EhAnuncio(valor)) deAnuncio = true;
            }
        }
        catch (JsonException) { /* cartão torto não conta, e não derruba a página */ }

        return (rastreado, deAnuncio);
    }

    /// <summary>
    /// Meta-Facebook, Meta-Instagram, Meta-WhatsApp e "WhatsApp anúncio" são mídia paga.
    /// Org-* é orgânico e fica de fora do denominador de propósito.
    /// </summary>
    private static bool EhAnuncio(string origem)
    {
        var o = origem.Trim();
        return o.StartsWith("Meta-", StringComparison.OrdinalIgnoreCase)
            || o.Contains("anúncio", StringComparison.OrdinalIgnoreCase)
            || o.Contains("anuncio", StringComparison.OrdinalIgnoreCase);
    }
}
