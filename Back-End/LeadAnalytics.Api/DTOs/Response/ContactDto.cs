using System.Text.Json.Serialization;

namespace LeadAnalytics.Api.DTOs.Response;

public class ContactDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("phone_normalized")]
    public string PhoneNormalized { get; set; } = string.Empty;

    public string Origem { get; set; } = "webhook_cloudia";
    public string? Etapa { get; set; }
    public List<string> Tags { get; set; } = new();

    [JsonPropertyName("last_message_at")]
    public DateTime? LastMessageAt { get; set; }

    public bool Blocked { get; set; }

    [JsonPropertyName("imported_at")]
    public DateTime? ImportedAt { get; set; }
}

public class ContactsListResponseDto
{
    public List<ContactDto> Data { get; set; } = new();
    public ContactPaginationDto Pagination { get; set; } = new();
    public ContactCountsDto Counts { get; set; } = new();
}

public class ContactPaginationDto
{
    public int Page { get; set; }

    [JsonPropertyName("page_size")]
    public int PageSize { get; set; }

    public int Total { get; set; }

    [JsonPropertyName("total_pages")]
    public int TotalPages { get; set; }
}

public class ContactCountsDto
{
    public int All { get; set; }

    [JsonPropertyName("webhook_cloudia")]
    public int WebhookCloudia { get; set; }

    [JsonPropertyName("import_csv")]
    public int ImportCsv { get; set; }
}

public class ContactImportResultDto
{
    [JsonPropertyName("batch_id")]
    public int BatchId { get; set; }

    public string Filename { get; set; } = string.Empty;

    [JsonPropertyName("total_rows")]
    public int TotalRows { get; set; }

    public int Created { get; set; }
    public int Updated { get; set; }
    public int Skipped { get; set; }
    public int Errors { get; set; }

    [JsonPropertyName("error_samples")]
    public List<ContactImportErrorDto> ErrorSamples { get; set; } = new();
}

public class ContactImportErrorDto
{
    public int Row { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string? Value { get; set; }
}

public class ContactDetailDto
{
    public string Id { get; set; } = string.Empty;
    public int TenantId { get; set; }
    public string Source { get; set; } = "contact"; // "contact" | "lead"

    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("phone_normalized")]
    public string PhoneNormalized { get; set; } = string.Empty;

    [JsonPropertyName("phone_raw")]
    public string? PhoneRaw { get; set; }

    public string Origem { get; set; } = "webhook_cloudia";

    // ─── Campos do Contact importado ───
    public string? Conexao { get; set; }
    public string? Observacoes { get; set; }
    public string? Etapa { get; set; }
    public List<string> Tags { get; set; } = new();

    [JsonPropertyName("meta_ads_ids")]
    public List<string> MetaAdsIds { get; set; } = new();

    [JsonPropertyName("consultation_at")]
    public DateTime? ConsultationAt { get; set; }

    [JsonPropertyName("consultation_registered_at")]
    public DateTime? ConsultationRegisteredAt { get; set; }

    [JsonPropertyName("last_message_at")]
    public DateTime? LastMessageAt { get; set; }

    public DateTime? Birthday { get; set; }

    public bool Blocked { get; set; }

    [JsonPropertyName("imported_at")]
    public DateTime? ImportedAt { get; set; }

    [JsonPropertyName("import_batch_id")]
    public int? ImportBatchId { get; set; }

    // ─── Campos do Lead (webhook_cloudia) ───
    [JsonPropertyName("external_id")]
    public int? ExternalId { get; set; }

    public string? Email { get; set; }
    public string? Cpf { get; set; }
    public string? Gender { get; set; }
    public string? Channel { get; set; }
    public string? Campaign { get; set; }
    public string? Ad { get; set; }

    [JsonPropertyName("tracking_confidence")]
    public string? TrackingConfidence { get; set; }

    [JsonPropertyName("current_stage")]
    public string? CurrentStage { get; set; }

    [JsonPropertyName("has_appointment")]
    public bool? HasAppointment { get; set; }

    [JsonPropertyName("has_payment")]
    public bool? HasPayment { get; set; }

    [JsonPropertyName("has_health_insurance_plan")]
    public bool? HasHealthInsurancePlan { get; set; }

    [JsonPropertyName("conversation_state")]
    public string? ConversationState { get; set; }

    [JsonPropertyName("unit_id")]
    public int? UnitId { get; set; }

    [JsonPropertyName("unit_name")]
    public string? UnitName { get; set; }

    [JsonPropertyName("attendant_id")]
    public int? AttendantId { get; set; }

    [JsonPropertyName("attendant_name")]
    public string? AttendantName { get; set; }

    [JsonPropertyName("attendant_email")]
    public string? AttendantEmail { get; set; }

    [JsonPropertyName("converted_at")]
    public DateTime? ConvertedAt { get; set; }

    // ─── Auditoria ───
    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("updated_at")]
    public DateTime UpdatedAt { get; set; }
}
