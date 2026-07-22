namespace LeadAnalytics.Api.DTOs.Ads;

/// <summary>
/// Payload que o n8n envia após puxar o gasto do Graph do Meta. O n8n manda o
/// gasto já pronto por conta; a API resolve a conta pelo <see cref="ExternalAccountId"/>
/// e faz upsert em CampaignDailySpend (a mesma tabela que o dashboard lê).
/// </summary>
public class AdsSpendIngestRequest
{
    /// <summary>meta | google.</summary>
    public string Provider { get; set; } = "meta";

    /// <summary>ID da conta no provedor (ex.: o account_id do Meta, sem o prefixo "act_").</summary>
    public string ExternalAccountId { get; set; } = "";

    public List<AdsSpendIngestRow> Rows { get; set; } = [];
}

public class AdsSpendIngestRow
{
    public string CampaignId { get; set; } = "";
    public string? CampaignName { get; set; }
    public DateOnly Date { get; set; }
    public decimal Spend { get; set; }
    /// <summary>Moeda ISO (BRL, USD…). Se vazio, mantém a existente / default BRL.</summary>
    public string? Currency { get; set; }
}

/// <summary>Resultado da ingestão de uma conta.</summary>
public class AdsSpendIngestResult
{
    /// <summary>false = nenhuma AdAccount casou com Provider+ExternalAccountId (precisa mapear).</summary>
    public bool Matched { get; set; }
    public int? AccountId { get; set; }
    public string ExternalAccountId { get; set; } = "";
    public int Upserted { get; set; }
}
