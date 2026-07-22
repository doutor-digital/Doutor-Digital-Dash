namespace LeadAnalytics.Api.Models;

/// <summary>
/// Investimento de UMA campanha em UM dia, puxado da API de Ads (Meta/Google) e gravado no
/// nosso banco pela <see cref="Service.Ads.AdsSpendSyncService"/>. É a fonte do "investimento"
/// do dashboard de Desempenho — não vem do lead. Único por (AdAccountId, CampaignId, Date).
/// </summary>
public class CampaignDailySpend
{
    public int Id { get; set; }

    public int ClinicId { get; set; }

    /// <summary>Conta de anúncios de origem.</summary>
    public int AdAccountId { get; set; }
    public AdAccount? AdAccount { get; set; }

    /// <summary><c>meta</c> | <c>google</c> (denormalizado p/ filtrar sem join).</summary>
    public string Provider { get; set; } = "meta";

    public string CampaignId { get; set; } = "";
    public string? CampaignName { get; set; }

    /// <summary>Dia do gasto (sem hora).</summary>
    public DateOnly Date { get; set; }

    /// <summary>Valor gasto no dia.</summary>
    public decimal Spend { get; set; }

    public string Currency { get; set; } = "BRL";

    // ── Métricas de entrega (vêm do insights junto com o gasto) ──────────────
    // Guardamos os contadores CRUS. CTR/CPC/CPM são derivados na hora de exibir
    // (razões não somam: a média de CTRs ≠ CTR do total), então não persistimos.

    /// <summary>Impressões no dia.</summary>
    public long Impressions { get; set; }

    /// <summary>Cliques no dia.</summary>
    public long Clicks { get; set; }

    /// <summary>Alcance (pessoas únicas). Não é somável entre dias — use com cuidado.</summary>
    public long Reach { get; set; }

    /// <summary>
    /// Conversas de WhatsApp iniciadas (action <c>messaging_conversation_started_7d</c>).
    /// É o "lead" na visão do Meta — o lead real é o que chega no Kommo.
    /// </summary>
    public int Conversations { get; set; }

    /// <summary>Quando este registro foi sincronizado por último.</summary>
    public DateTime SyncedAt { get; set; }
}
