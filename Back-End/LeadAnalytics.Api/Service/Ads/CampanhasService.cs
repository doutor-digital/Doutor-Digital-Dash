using System.Text.Json;
using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.DTOs.Saude;
using Microsoft.EntityFrameworkCore;

namespace LeadAnalytics.Api.Service.Ads;

/// <summary>
/// Campanhas do período: quantos leads trouxeram, quanto custaram e qual anúncio puxou.
///
/// POR QUE ISTO NÃO EXISTIA
/// ------------------------
/// A IA respondia "Não tenho esse dado" para "qual campanha trouxe mais leads". Não era falha
/// dela: o lead guarda o ID DO ANÚNCIO, e o gasto vem por CAMPANHA — ninguém ligava os dois.
/// A ponte é o cache de criativos, que agora guarda a campanha de cada anúncio.
///
/// O CUSTO POR LEAD SÓ APARECE QUANDO OS DOIS LADOS EXISTEM
/// --------------------------------------------------------
/// Campanha com gasto e sem lead atribuído mostra o gasto e deixa o custo em branco. Dividir
/// por zero, ou esconder a campanha, produziria um ranking que mente por omissão: campanha
/// cara sem retorno é exatamente a que precisa aparecer.
/// </summary>
public class CampanhasService(AppDbContext db)
{
    private readonly AppDbContext _db = db;

    /// <summary>Campo "⌂ ID do anúncio" da Kommo — é o que liga o lead ao criativo.</summary>
    private const string NomeCampoAnuncio = "id do anúncio";

    public async Task<List<CampanhaDto>> GetAsync(
        int tenantId, int? unitId, DateTime de, DateTime ate, CancellationToken ct = default)
    {
        // ── 1. Leads do período, com o id do anúncio que os trouxe ──────────
        var leads = await _db.Leads.AsNoTracking().ExcludeDeleted()
            .Where(l => l.TenantId == tenantId
                        && (!unitId.HasValue || l.UnitId == unitId.Value)
                        && (l.OriginalCreatedAt ?? l.CreatedAt) >= de
                        && (l.OriginalCreatedAt ?? l.CreatedAt) <= ate
                        && l.CustomFieldsJson != null)
            .Select(l => new { l.Id, l.CustomFieldsJson, l.CurrentStage })
            .ToListAsync(ct);

        var porAnuncio = new Dictionary<string, (int leads, int agendados)>();
        foreach (var l in leads)
        {
            var idAnuncio = LerIdAnuncio(l.CustomFieldsJson);
            if (idAnuncio is null) continue;

            // Um lead pode trazer vários ids separados por vírgula (múltiplo toque).
            // Conta em todos: esconder o toque anterior superestima o último clique.
            foreach (var id in idAnuncio.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var agendou = (l.CurrentStage ?? "").Contains("AGENDADO", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
                var atual = porAnuncio.GetValueOrDefault(id);
                porAnuncio[id] = (atual.leads + 1, atual.agendados + agendou);
            }
        }

        // ── 2. Anúncio → campanha, pelo cache de criativos ──────────────────
        var ids = porAnuncio.Keys.ToList();
        var criativos = ids.Count == 0
            ? []
            : await _db.AdCreatives.AsNoTracking()
                .Where(c => ids.Contains(c.AdId))
                .ToListAsync(ct);

        // ── 3. Gasto por campanha no período ────────────────────────────────
        var dDe = DateOnly.FromDateTime(de);
        var dAte = DateOnly.FromDateTime(ate);
        var gastos = await _db.CampaignDailySpends.AsNoTracking()
            .Where(g => g.Date >= dDe && g.Date <= dAte
                        && g.AdAccount!.ClinicId == tenantId
                        && (!unitId.HasValue || g.AdAccount.UnitId == null || g.AdAccount.UnitId == unitId.Value))
            .GroupBy(g => new { g.CampaignId, g.CampaignName })
            .Select(g => new
            {
                g.Key.CampaignId,
                g.Key.CampaignName,
                Gasto = g.Sum(x => x.Spend),
                Impressoes = g.Sum(x => x.Impressions),
                Cliques = g.Sum(x => x.Clicks),
            })
            .ToListAsync(ct);

        // ── 4. Junta ────────────────────────────────────────────────────────
        var mapa = new Dictionary<string, CampanhaDto>();

        CampanhaDto Obter(string id, string? nome)
        {
            if (!mapa.TryGetValue(id, out var c))
            {
                c = new CampanhaDto { CampanhaId = id, Nome = nome };
                mapa[id] = c;
            }
            if (string.IsNullOrWhiteSpace(c.Nome) && !string.IsNullOrWhiteSpace(nome)) c.Nome = nome;
            return c;
        }

        foreach (var g in gastos)
        {
            var c = Obter(g.CampaignId, g.CampaignName);
            c.Gasto = g.Gasto;
            c.Impressoes = g.Impressoes;
            c.Cliques = g.Cliques;
        }

        foreach (var (idAnuncio, dados) in porAnuncio)
        {
            var cr = criativos.FirstOrDefault(x => x.AdId == idAnuncio);

            // Sem campanha conhecida o anúncio vira uma linha própria: perder o lead do
            // ranking seria pior que mostrar uma campanha "(não identificada)".
            var campId = cr?.CampaignId ?? $"anuncio:{idAnuncio}";
            var campNome = cr?.CampaignName;

            var c = Obter(campId, campNome);
            c.Leads += dados.leads;
            c.Agendados += dados.agendados;

            // O anúncio que mais trouxe vira a cara da campanha no card.
            if (dados.leads > c.MelhorAnuncioLeads)
            {
                c.MelhorAnuncioLeads = dados.leads;
                c.MelhorAnuncioId = idAnuncio;
                c.MelhorAnuncioNome = cr?.Name;
                c.MelhorAnuncioImagem = cr?.ThumbnailUrl;
            }
        }

        foreach (var c in mapa.Values)
        {
            if (string.IsNullOrWhiteSpace(c.Nome))
                c.Nome = c.CampanhaId.StartsWith("anuncio:") ? "Campanha não identificada" : c.CampanhaId;

            // Custo por lead só quando os dois lados existem — ver o resumo da classe.
            c.CustoPorLead = c.Leads > 0 && c.Gasto > 0
                ? Math.Round(c.Gasto / c.Leads, 2)
                : null;
        }

        return [.. mapa.Values.OrderByDescending(c => c.Leads).ThenByDescending(c => c.Gasto)];
    }

    private static string? LerIdAnuncio(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;
            foreach (var f in doc.RootElement.EnumerateArray())
            {
                if (!f.TryGetProperty("field_name", out var n) || n.ValueKind != JsonValueKind.String) continue;
                var nome = (n.GetString() ?? "").ToLowerInvariant();
                if (!nome.Contains(NomeCampoAnuncio)) continue;
                if (!f.TryGetProperty("value", out var v)) continue;
                var val = v.ValueKind == JsonValueKind.String ? v.GetString() : v.ToString();
                return string.IsNullOrWhiteSpace(val) ? null : val;
            }
        }
        catch (JsonException) { /* cartão torto não derruba o ranking */ }
        return null;
    }
}
