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

    [JsonPropertyName("is_custom")]
    public bool IsCustom { get; set; }

    [JsonPropertyName("display_name")]
    public string? DisplayName { get; set; }

    [JsonPropertyName("accent_color")]
    public string? AccentColor { get; set; }

    [JsonPropertyName("display_type")]
    public string DisplayType { get; set; } = "number";

    [JsonPropertyName("sort_order")]
    public int SortOrder { get; set; }

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

    /// <summary>KPI criado do zero (chave arbitrária + nome/cor próprios).</summary>
    [JsonPropertyName("is_custom")]
    public bool IsCustom { get; set; }

    [JsonPropertyName("display_name")]
    public string? DisplayName { get; set; }

    [JsonPropertyName("accent_color")]
    public string? AccentColor { get; set; }

    [JsonPropertyName("display_type")]
    public string DisplayType { get; set; } = "number";

    [JsonPropertyName("sort_order")]
    public int SortOrder { get; set; }
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

    /// <summary>Data/hora marcada do agendamento (Lead.AppointmentScheduledAt).</summary>
    [JsonPropertyName("appointment_at")] public DateTime? AppointmentAt { get; set; }

    /// <summary>Valor da consulta (Lead.ConsultationValue).</summary>
    [JsonPropertyName("consultation_value")] public decimal? ConsultationValue { get; set; }

    /// <summary>Lead fechou tratamento (Lead.ClosedTreatment).</summary>
    [JsonPropertyName("closed_treatment")] public bool? ClosedTreatment { get; set; }

    // ── Campos extraídos do CustomFieldsJson ────────────────────────────────
    [JsonPropertyName("motivo_nao_agendamento")] public string? MotivoNaoAgendamento { get; set; }
    [JsonPropertyName("tratamento_fechado")] public string? TratamentoFechado { get; set; }
    [JsonPropertyName("responsavel_agendamento")] public string? ResponsavelAgendamento { get; set; }
    [JsonPropertyName("qualificacao")] public string? Qualificacao { get; set; }
    /// <summary>Origem custom-field (separada de Source/Channel do Kommo).</summary>
    [JsonPropertyName("origem_custom")] public string? OrigemCustom { get; set; }
    /// <summary>Valor do tratamento (custom field "valor tratamento").</summary>
    [JsonPropertyName("treatment_value")] public decimal? TreatmentValue { get; set; }

    /// <summary>Lead foi marcado como "não contar" pelo admin neste KPI (kpi_exclusions).</summary>
    [JsonPropertyName("excluded")] public bool Excluded { get; set; }

    /// <summary>Id da transição de etapa mais recente que casou com a fonte do KPI (LeadStageHistory).
    /// Permite o front chamar PATCH /api/admin/stage-history/{id}/corrected-date direto do drill-down.
    /// Só populado para fontes que derivam de stage_history (KommoStage).</summary>
    [JsonPropertyName("history_id")] public int? HistoryId { get; set; }

    /// <summary>Data efetiva da transição (CorrectedChangedAt ?? ChangedAt) — o que o KPI usa pra contar.</summary>
    [JsonPropertyName("effective_changed_at")] public DateTime? EffectiveChangedAt { get; set; }

    /// <summary>Todos os campos customizados PREENCHIDOS do lead (nome + valor), pro drill-down
    /// mostrar "campos preenchidos" sem precisar abrir o lead. Ordem = a do CustomFieldsJson.</summary>
    [JsonPropertyName("custom_fields")] public List<KpiLeadFieldDto> CustomFields { get; set; } = new();
}

/// <summary>Par nome/valor de um campo customizado preenchido do lead (drill-down).</summary>
public class KpiLeadFieldDto
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("value")] public string Value { get; set; } = "";
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

/// <summary>Mapeamento dos campos do Perfil do Lead (ids de campo da Kommo) por unidade.</summary>
public class LeadProfileConfigDto
{
    [JsonPropertyName("birthdate_field_id")] public long? BirthdateFieldId { get; set; }
    [JsonPropertyName("appointment_field_id")] public long? AppointmentFieldId { get; set; }
    [JsonPropertyName("doctor_field_id")] public long? DoctorFieldId { get; set; }
    // ── Mapeamentos p/ os breakdowns dos KPI cards ─────────────────────────
    [JsonPropertyName("origem_field_id")] public long? OrigemFieldId { get; set; }
    [JsonPropertyName("motivo_nao_agendamento_field_id")] public long? MotivoNaoAgendamentoFieldId { get; set; }
    [JsonPropertyName("fisioterapeuta_field_id")] public long? FisioterapeutaFieldId { get; set; }
    [JsonPropertyName("valor_tratamento_field_id")] public long? ValorTratamentoFieldId { get; set; }
    [JsonPropertyName("valor_consulta_field_id")] public long? ValorConsultaFieldId { get; set; }
    [JsonPropertyName("tratamento_fechado_field_id")] public long? TratamentoFechadoFieldId { get; set; }
    [JsonPropertyName("qualificacao_field_id")] public long? QualificacaoFieldId { get; set; }
    /// <summary>Campo "Tipo" (resgate/ligação/mensagem) — breakdown do card Resgate.</summary>
    [JsonPropertyName("tipo_field_id")] public long? TipoFieldId { get; set; }
    /// <summary>Campo "Tipo de agendamento" (consulta/retorno/avaliação) — breakdown do card Agendados.</summary>
    [JsonPropertyName("tipo_agendamento_field_id")] public long? TipoAgendamentoFieldId { get; set; }
    /// <summary>Campo "Tipo de tratamento" (fisioterapia/pilates/...) — breakdown do card Tratamentos.</summary>
    [JsonPropertyName("tipo_tratamento_field_id")] public long? TipoTratamentoFieldId { get; set; }
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
