using System.Text.Json.Serialization;

namespace LeadAnalytics.Api.DTOs.Response;

/// <summary>
/// Pipeline da Kommo com lista de statuses, retornado pelo endpoint
/// GET /units/{id}/kommo-pipelines para o front traduzir status_id em nome.
/// </summary>
public class KommoPipelineDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("is_main")]
    public bool IsMain { get; set; }

    public List<KommoPipelineStatusDto> Statuses { get; set; } = new();
}

public class KommoPipelineStatusDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Color { get; set; }
    public int Type { get; set; }

    [JsonPropertyName("pipeline_id")]
    public long PipelineId { get; set; }
}
