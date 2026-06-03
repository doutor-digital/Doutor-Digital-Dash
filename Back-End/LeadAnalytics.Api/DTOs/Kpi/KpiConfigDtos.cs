using System.Text.Json;
using System.Text.Json.Serialization;

namespace LeadAnalytics.Api.DTOs.Kpi;

/// <summary>Um KPI disponível para mapeamento (catálogo enviado ao front).</summary>
public class KpiCatalogItemDto
{
    public string Key { get; set; } = null!;
    public string Label { get; set; } = null!;
    public string Description { get; set; } = string.Empty;
}

/// <summary>Configuração salva de um KPI (leitura).</summary>
public class KpiConfigItemDto
{
    [JsonPropertyName("kpi_key")]
    public string KpiKey { get; set; } = null!;

    [JsonPropertyName("source_type")]
    public string SourceType { get; set; } = null!;

    /// <summary>Parâmetros da fonte (stageIds / fieldId / matchValues…), JSON livre.</summary>
    public JsonElement Config { get; set; }

    [JsonPropertyName("updated_by_email")]
    public string? UpdatedByEmail { get; set; }

    [JsonPropertyName("updated_at")]
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>Item para salvar (upsert) na PUT.</summary>
public class KpiConfigUpsertItemDto
{
    [JsonPropertyName("kpi_key")]
    public string KpiKey { get; set; } = null!;

    [JsonPropertyName("source_type")]
    public string SourceType { get; set; } = null!;

    public JsonElement Config { get; set; }
}

/// <summary>Corpo da PUT /api/config/kpis — lista de mapeamentos a salvar.</summary>
public class KpiConfigSaveRequestDto
{
    public List<KpiConfigUpsertItemDto> Items { get; set; } = new();
}

/// <summary>Corpo da POST /api/config/kpis/preview — calcula o número ao vivo.</summary>
public class KpiPreviewRequestDto
{
    [JsonPropertyName("source_type")]
    public string SourceType { get; set; } = null!;

    public JsonElement Config { get; set; }

    [JsonPropertyName("date_from")]
    public DateTime? DateFrom { get; set; }

    [JsonPropertyName("date_to")]
    public DateTime? DateTo { get; set; }
}

/// <summary>Resultado do preview.</summary>
public class KpiPreviewResponseDto
{
    /// <summary>Número final do KPI (contagem ou soma).</summary>
    public double Value { get; set; }

    /// <summary>Quantos leads foram avaliados no período (universo da conta).</summary>
    [JsonPropertyName("sample_size")]
    public int SampleSize { get; set; }

    /// <summary>Mensagem opcional (ex.: aviso de configuração incompleta).</summary>
    public string? Note { get; set; }
}
