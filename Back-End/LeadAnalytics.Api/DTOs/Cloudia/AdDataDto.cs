using System.Text.Json.Serialization;

namespace LeadAnalytics.Api.DTOs.Cloudia;

using System.Text.Json.Serialization;

public class AdDataDto
{
    [JsonPropertyName("ad_id")]
    public string? AdId { get; set; }

    [JsonPropertyName("ad_name")]
    public string? AdName { get; set; }

    [JsonPropertyName("source")]
    public string? Source { get; set; }

    // ✅ Mapeamento para compatibilidade
    [JsonPropertyName("id")]
    public string? Id { get; set; }
}