using System.ComponentModel.DataAnnotations;

namespace LeadAnalytics.Api.Models;

public class WebhookEvent
{
    public int Id { get; set; }
    [Required]
    public string Provider { get; set; }
    [Required]
    public string EventType { get; set; }
    [Required]
    public string PayloadJson { get; set; } = null!;
    public string? PhoneNumberId { get; set; }
    public int? TenantId { get; set; }
    public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;
}