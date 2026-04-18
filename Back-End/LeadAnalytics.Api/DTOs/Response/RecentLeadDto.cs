using System.Text.Json.Serialization;

namespace LeadAnalytics.Api.DTOs.Response;

public class RecentLeadDto
{
    public int Id { get; set; }

    [JsonPropertyName("external_id")]
    public int ExternalId { get; set; }

    public string Name { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Source { get; set; }
    public string? Channel { get; set; }

    [JsonPropertyName("current_stage")]
    public string? CurrentStage { get; set; }

    [JsonPropertyName("conversation_state")]
    public string? ConversationState { get; set; }

    [JsonPropertyName("unit_id")]
    public int? UnitId { get; set; }

    [JsonPropertyName("unit_name")]
    public string? UnitName { get; set; }

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; }
}

public class RecentLeadsResponseDto
{
    public int Hours { get; set; }
    public int Total { get; set; }

    [JsonPropertyName("since")]
    public DateTime Since { get; set; }

    public List<RecentLeadDto> Items { get; set; } = new();
}
