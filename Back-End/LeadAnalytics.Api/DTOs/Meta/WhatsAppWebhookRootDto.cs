using System.Text.Json.Serialization;

namespace LeadAnalytics.Api.DTOs.Meta;

public class WhatsAppWebhookRootDto
{
    [JsonPropertyName("object")]
    public string? Object { get; set; }

    [JsonPropertyName("entry")]
    public List<WhatsAppWebhookEntryDto>? Entry { get; set; }
}

public class WhatsAppWebhookEntryDto
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("changes")]
    public List<WhatsAppWebhookChangeDto>? Changes { get; set; }
}

public class WhatsAppWebhookChangeDto
{
    [JsonPropertyName("field")]
    public string? Field { get; set; }

    [JsonPropertyName("value")]
    public WhatsAppWebhookValueDto? Value { get; set; }
}

public class WhatsAppWebhookValueDto
{
    [JsonPropertyName("messaging_product")]
    public string? MessagingProduct { get; set; }

    [JsonPropertyName("metadata")]
    public WaMetadataDto? Metadata { get; set; }

    [JsonPropertyName("contacts")]
    public List<WaContactDto>? Contacts { get; set; }

    [JsonPropertyName("messages")]
    public List<WaMessageDto>? Messages { get; set; }
}

public class WaMetadataDto
{
    [JsonPropertyName("display_phone_number")]
    public string? DisplayPhoneNumber { get; set; }

    [JsonPropertyName("phone_number_id")]
    public string? PhoneNumberId { get; set; }
}

public class WaContactDto
{
    [JsonPropertyName("profile")]
    public WaProfileDto? Profile { get; set; }

    [JsonPropertyName("wa_id")]
    public string? WaId { get; set; }

    [JsonPropertyName("user_id")]
    public string? UserId { get; set; }
}

public class WaProfileDto
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

public class WaMessageDto
{
    [JsonPropertyName("from")]
    public string? From { get; set; }

    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("timestamp")]
    public string? Timestamp { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("text")]
    public WaTextDto? Text { get; set; }

    [JsonPropertyName("referral")]
    public WaReferralDto? Referral { get; set; }

    [JsonPropertyName("context")]
    public WaContextDto? Context { get; set; }

    [JsonPropertyName("from_user_id")]
    public string? FromUserId { get; set; }
}

public class WaTextDto
{
    [JsonPropertyName("body")]
    public string? Body { get; set; }
}

public class WaReferralDto
{
    [JsonPropertyName("source_url")]
    public string? SourceUrl { get; set; }

    [JsonPropertyName("source_type")]
    public string? SourceType { get; set; }

    [JsonPropertyName("source_id")]
    public string? SourceId { get; set; }

    [JsonPropertyName("headline")]
    public string? Headline { get; set; }

    [JsonPropertyName("body")]
    public string? Body { get; set; }

    [JsonPropertyName("ctwa_clid")]
    public string? CtwaClid { get; set; }
}

public class WaContextDto
{
    [JsonPropertyName("from")]
    public string? From { get; set; }

    [JsonPropertyName("id")]
    public string? Id { get; set; }
}