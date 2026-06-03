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

/// <summary>Corpo da POST .../kpi-leads — drill-down: os leads por trás de um KPI.</summary>
public class KpiLeadsRequestDto
{
    [JsonPropertyName("kpi_key")]
    public string? KpiKey { get; set; }

    /// <summary>Opcional: sobrepõe a fonte salva (ex.: drill-down de um rascunho).</summary>
    [JsonPropertyName("source_type")]
    public string? SourceType { get; set; }

    public JsonElement Config { get; set; }

    [JsonPropertyName("date_from")]
    public DateTime? DateFrom { get; set; }

    [JsonPropertyName("date_to")]
    public DateTime? DateTo { get; set; }
}

/// <summary>Um lead na lista de drill-down de um KPI.</summary>
public class KpiLeadDto
{
    public int Id { get; set; }
    [JsonPropertyName("external_id")] public int ExternalId { get; set; }
    public string Name { get; set; } = "";
    public string? Phone { get; set; }
    public string? Source { get; set; }
    public string? Channel { get; set; }
    [JsonPropertyName("current_stage")] public string? CurrentStage { get; set; }
    [JsonPropertyName("current_stage_id")] public int? CurrentStageId { get; set; }
    [JsonPropertyName("lead_type")] public string? LeadType { get; set; }
    [JsonPropertyName("has_appointment")] public bool HasAppointment { get; set; }
    [JsonPropertyName("has_payment")] public bool HasPayment { get; set; }
    [JsonPropertyName("created_at")] public DateTime CreatedAt { get; set; }
    /// <summary>Valor do campo customizado que casou (quando a fonte é por campo).</summary>
    [JsonPropertyName("matched_value")] public string? MatchedValue { get; set; }
}

public class KpiLeadsResponseDto
{
    public List<KpiLeadDto> Items { get; set; } = new();
    public int Total { get; set; }
    public bool Truncated { get; set; }
    /// <summary>Mensagem opcional (ex.: KPI sem fonte configurada).</summary>
    public string? Note { get; set; }
}

/// <summary>Contagem de um valor dentro de um campo customizado.</summary>
public class CustomFieldValueCountDto
{
    public string Value { get; set; } = "";
    public int Count { get; set; }
}

/// <summary>Métrica de um campo customizado: preenchimento + distribuição de valores.</summary>
public class CustomFieldSummaryDto
{
    [JsonPropertyName("field_id")] public long FieldId { get; set; }
    [JsonPropertyName("field_name")] public string FieldName { get; set; } = "";
    [JsonPropertyName("field_code")] public string? FieldCode { get; set; }
    public string Type { get; set; } = "text";
    /// <summary>Quantos leads do período têm esse campo preenchido.</summary>
    public int Filled { get; set; }
    [JsonPropertyName("distinct_values")] public int DistinctValues { get; set; }
    [JsonPropertyName("top_values")] public List<CustomFieldValueCountDto> TopValues { get; set; } = new();
}

public class CustomFieldsSummaryResponseDto
{
    [JsonPropertyName("total_leads")] public int TotalLeads { get; set; }
    public List<CustomFieldSummaryDto> Fields { get; set; } = new();
    public bool Truncated { get; set; }
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
