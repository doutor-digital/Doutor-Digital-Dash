namespace LeadAnalytics.Api.DTOs.Meta;

public class CreateOriginEventDto
{
    public string Phone { get; set; } = null!;
    public string? ContactName { get; set; }
    public string CtwaClid { get; set; } = null!;
    public string? SourceId { get; set; }
    public string? SourceType { get; set; }
    public string? SourceUrl { get; set; }
    public string? Headline { get; set; }
    public string? Body { get; set; }
    public string? MessageId { get; set; }
    public DateTime? MessageTimestamp { get; set; }
    public int? TenantId { get; set; }
    public int? WebhookEventId { get; set; }
}