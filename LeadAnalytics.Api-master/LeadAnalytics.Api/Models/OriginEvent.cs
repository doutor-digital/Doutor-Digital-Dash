using System.ComponentModel.DataAnnotations;

namespace LeadAnalytics.Api.Models;

public class OriginEvent
{
    public int Id { get; set; }

    [Required]
    [MaxLength(20)]
    public string Phone { get; set; } = null!;

    [MaxLength(255)]
    public string? ContactName { get; set; }

    [MaxLength(255)]
    public string CtwaClid { get; set; } = null!;

    [MaxLength(100)]
    public string? SourceId { get; set; }

    [MaxLength(100)]
    public string? SourceType { get; set; }

    [MaxLength(1000)]
    public string? SourceUrl { get; set; }

    [MaxLength(100)]
    public string? Headline { get; set; }

    [MaxLength(1000)]
    public string? Body { get; set; }

    [MaxLength(100)]
    public string? MessageId { get; set; }

    public DateTime? MessageTimestamp { get; set; }

    public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;
    public bool Processed { get; set; } = false;
    public int? TenantId { get; set; }
    public int? WebhookEventId { get; set; }
    public WebhookEvent? WebhookEvent { get; set; }
    public string Confidence { get; set; } = "LOW"; // HIGH | MEDIUM | LOW

}