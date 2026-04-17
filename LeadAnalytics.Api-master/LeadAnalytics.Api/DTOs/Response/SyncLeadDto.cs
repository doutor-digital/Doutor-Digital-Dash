using System.Text.Json.Serialization;

namespace LeadAnalytics.Api.DTOs.Response;

public class SyncLeadDto
{
    [JsonPropertyName("externalId")]
    public int ExternalId { get; set; }

    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = [];

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("phone")]
    public string? Phone { get; set; }

    [JsonPropertyName("stage")]
    public string? Stage { get; set; }

    [JsonPropertyName("tenantId")]
    public int TenantId { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTime? CreatedAt { get; set; }

    [JsonPropertyName("updatedAt")]
    public DateTime? UpdatedAt { get; set; }
}