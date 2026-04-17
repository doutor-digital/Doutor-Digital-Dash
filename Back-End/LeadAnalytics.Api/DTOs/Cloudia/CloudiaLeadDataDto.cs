using System.Text.Json.Serialization;

namespace LeadAnalytics.Api.DTOs.Cloudia;

public class CloudiaLeadDataDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("clinic_id")]  // ✅ CRÍTICO!
    public int ClinicId { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("phone")]
    public string? Phone { get; set; }

    [JsonPropertyName("email")]
    public string? Email { get; set; }

    [JsonPropertyName("cpf")]
    public string? Cpf { get; set; }

    [JsonPropertyName("gender")]
    public string? Gender { get; set; }

    [JsonPropertyName("origin")]
    public string? Origin { get; set; }

    [JsonPropertyName("has_health_insurance_plan")]
    public bool? HasHealthInsurancePlan { get; set; }

    [JsonPropertyName("created_at")]
    public DateTime? CreatedAt { get; set; }

    [JsonPropertyName("last_updated_at")]
    public DateTime? LastUpdatedAt { get; set; }

    [JsonPropertyName("observations")]
    public string? Observations { get; set; }

    [JsonPropertyName("ad_data")]
    public List<AdDataDto>? AdData { get; set; }

    [JsonPropertyName("last_ad_id")]
    public string? LastAdId { get; set; }

    [JsonPropertyName("id_channel_integration")]
    public int? IdChannelIntegration { get; set; }

    [JsonPropertyName("idfacebookapp")]
    public string? IdFacebookApp { get; set; }

    [JsonPropertyName("stage")]
    public string? Stage { get; set; }

    [JsonPropertyName("id_stage")]
    public int? IdStage { get; set; }

    [JsonPropertyName("conversationState")]
    public string? ConversationState { get; set; }

    [JsonPropertyName("id_whatsapp")]
    public string? IdWhatsApp { get; set; }

    [JsonPropertyName("registered_on_whatsapp")]
    public int? RegisteredOnWhatsApp { get; set; }

    [JsonPropertyName("tags")]
    public List<TagDto>? Tags { get; set; }
}