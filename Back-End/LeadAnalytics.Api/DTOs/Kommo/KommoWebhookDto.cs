using System.Text.Json.Serialization;

namespace LeadAnalytics.Api.DTOs.Kommo;

public class KommoWebhookDto
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("phone")]
    public string? Phone { get; set; }

    [JsonPropertyName("status_id")]
    public string? StatusId { get; set; }

    [JsonPropertyName("responsible_user_id")]
    public string? ResponsibleUserId { get; set; }
}
