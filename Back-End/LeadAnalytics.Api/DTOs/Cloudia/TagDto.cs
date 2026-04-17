using System.Text.Json.Serialization;

namespace LeadAnalytics.Api.DTOs.Cloudia;

public class TagDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = null!;
}
