using System.Text.Json.Serialization;

namespace LeadAnalytics.Api.DTOs.Meta;

public class WhatsAppEventDto
{
    [JsonPropertyName("display_phone_number")]
    public string Phone { get; set; }
    public List<WaMessageDto> Message { get; set; }
    public DateTime Timestamp { get; set; }
    [JsonPropertyName("ctwa_clid")]
    public string CtwaClid { get; set; }
    [JsonPropertyName("source_id")]
    public string SourceId { get; set; }
    [JsonPropertyName("source_type")]
    public string SourceType { get; set; }
}
