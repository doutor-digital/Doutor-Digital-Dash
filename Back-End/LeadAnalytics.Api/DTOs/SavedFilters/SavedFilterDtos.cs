using System.Text.Json;
using System.Text.Json.Serialization;

namespace LeadAnalytics.Api.DTOs.SavedFilters;

/// <summary>Filtro dinâmico salvo (leitura).</summary>
public class SavedFilterItemDto
{
    public int Id { get; set; }

    public string Name { get; set; } = null!;

    /// <summary>Payload completo do filtro (rangeKey, horas, origem, atendente, etapas…), JSON livre.</summary>
    public JsonElement Filter { get; set; }

    [JsonPropertyName("sort_order")]
    public int SortOrder { get; set; }

    [JsonPropertyName("updated_by_email")]
    public string? UpdatedByEmail { get; set; }

    [JsonPropertyName("updated_at")]
    public DateTime UpdatedAt { get; set; }
}

/// <summary>Corpo para criar/atualizar um filtro salvo.</summary>
public class SavedFilterSaveRequestDto
{
    public string Name { get; set; } = null!;

    /// <summary>Payload completo do filtro. JSON livre — o front define o shape.</summary>
    public JsonElement Filter { get; set; }

    [JsonPropertyName("sort_order")]
    public int SortOrder { get; set; }
}
