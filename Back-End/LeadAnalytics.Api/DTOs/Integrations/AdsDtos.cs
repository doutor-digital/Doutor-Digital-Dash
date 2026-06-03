using System.Text.Json.Serialization;

namespace LeadAnalytics.Api.DTOs.Integrations;

/// <summary>Conta de Ads conectada (leitura — NUNCA inclui tokens).</summary>
public class AdAccountDto
{
    public int Id { get; set; }
    public string Provider { get; set; } = "";
    [JsonPropertyName("external_account_id")] public string? ExternalAccountId { get; set; }
    public string? Name { get; set; }
    public string Status { get; set; } = "";
    [JsonPropertyName("last_sync_at")] public DateTime? LastSyncAt { get; set; }
    [JsonPropertyName("last_sync_note")] public string? LastSyncNote { get; set; }
    /// <summary>True = provedor com credenciais reais; false = modo demo (stub).</summary>
    public bool Live { get; set; }
}

/// <summary>Corpo do PUT de credenciais. O segredo só é trocado se vier preenchido.</summary>
public class AdsCredentialsSaveDto
{
    [JsonPropertyName("client_id")] public string? ClientId { get; set; }
    [JsonPropertyName("client_secret")] public string? ClientSecret { get; set; }
    [JsonPropertyName("developer_token")] public string? DeveloperToken { get; set; }
}

/// <summary>Gasto agregado de uma campanha no período (consumido pelo /desempenho).</summary>
public class AdsSpendItemDto
{
    public string Provider { get; set; } = "";
    [JsonPropertyName("campaign_id")] public string CampaignId { get; set; } = "";
    [JsonPropertyName("campaign_name")] public string? CampaignName { get; set; }
    public decimal Spend { get; set; }
    public string Currency { get; set; } = "BRL";
}
