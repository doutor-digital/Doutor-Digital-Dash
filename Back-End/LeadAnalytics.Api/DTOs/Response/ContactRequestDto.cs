using System.Text.Json.Serialization;

namespace LeadAnalytics.Api.DTOs.Response;

public class ContactCreateDto
{
    [JsonPropertyName("clinic_id")]
    public int ClinicId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Phone { get; set; } = string.Empty;

    public string? Conexao { get; set; }

    public string? Observacoes { get; set; }

    public string? Etapa { get; set; }

    public List<string>? Tags { get; set; }

    [JsonPropertyName("consultation_at")]
    public DateTime? ConsultationAt { get; set; }

    public DateTime? Birthday { get; set; }

    [JsonPropertyName("attendance_status")]
    public string? AttendanceStatus { get; set; }
}

public class ContactActionDto
{
    public string Action { get; set; } = string.Empty;

    [JsonPropertyName("consultation_at")]
    public DateTime? ConsultationAt { get; set; }

    public string? Observacoes { get; set; }
}

public class ContactFiltersDto
{
    public string Origem { get; set; } = "all";
    public string? Search { get; set; }

    public string? Status { get; set; }
    public string? Etapa { get; set; }
    public string? Tag { get; set; }
    public bool? Blocked { get; set; }

    [JsonPropertyName("has_consultation")]
    public bool? HasConsultation { get; set; }

    [JsonPropertyName("date_from")]
    public DateTime? DateFrom { get; set; }

    [JsonPropertyName("date_to")]
    public DateTime? DateTo { get; set; }
}
