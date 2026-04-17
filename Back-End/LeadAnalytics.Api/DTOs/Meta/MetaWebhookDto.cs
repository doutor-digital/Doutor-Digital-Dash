namespace LeadAnalytics.Api.DTOs.Meta;

/// <summary>
/// Payload do webhook da Meta (FONTE DA VERDADE)
/// </summary>
public class MetaWebhookDto
{
    public string Object { get; set; } = null!;
    public List<MetaEntryDto> Entry { get; set; } = [];
}

public class MetaEntryDto
{
    public string Id { get; set; } = null!;
    public List<MetaChangeDto> Changes { get; set; } = [];
}

public class MetaChangeDto
{
    public string Field { get; set; } = null!;
    public MetaValueDto Value { get; set; } = null!;
}

public class MetaValueDto
{
    public string MessagingProduct { get; set; } = null!;
    public MetaMetadataDto? Metadata { get; set; }
    public List<MetaContactDto>? Contacts { get; set; }
    public List<MetaMessageDto>? Messages { get; set; }
}

public class MetaMetadataDto
{
    public string DisplayPhoneNumber { get; set; } = null!;
    public string PhoneNumberId { get; set; } = null!;
}

public class MetaContactDto
{
    public MetaProfileDto Profile { get; set; } = null!;
    public string WaId { get; set; } = null!;
}

public class MetaProfileDto
{
    public string Name { get; set; } = null!;
}

public class MetaMessageDto
{
    public string From { get; set; } = null!;
    public string Id { get; set; } = null!;
    public string Timestamp { get; set; } = null!;
    public string Type { get; set; } = null!;
    public MetaContextDto? Context { get; set; }
    public MetaReferralDto? Referral { get; set; }
}

public class MetaContextDto
{
    public string? From { get; set; }
    public string? Id { get; set; }
}

public class MetaReferralDto
{
    public string? SourceUrl { get; set; }
    public string? SourceType { get; set; }
    public string? SourceId { get; set; }
    public string? Headline { get; set; }
    public string? Body { get; set; }
    public string? MediaType { get; set; }
    public string? ImageUrl { get; set; }
    public string? VideoUrl { get; set; }
    public string? ThumbnailUrl { get; set; }
    public string? CtwaClid { get; set; }
}