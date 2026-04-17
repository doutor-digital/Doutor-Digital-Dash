using System.ComponentModel.DataAnnotations;

namespace LeadAnalytics.Api.Models;

public class LeadAttribution
{
    public int Id { get; set; }

    public int LeadId { get; set; }

    [Required]
    [MaxLength(20)]
    public string Phone { get; set; } = null!;

    [MaxLength(255)]
    public string CtwaClid { get; set; } = null!;

    [MaxLength(100)]
    public string? SourceId { get; set; }

    [MaxLength(100)]
    public string? SourceType { get; set; }

    [MaxLength(100)]
    public string MatchType { get; set; } = "UNKNOWN"; // CTWA | SOCIAL | FALLBACK

    [MaxLength(50)]
    public string Confidence { get; set; } = "LOW";

    public DateTime MatchedAt { get; set; } = DateTime.UtcNow;

    public int OriginEventId { get; set; }
    public OriginEvent OriginEvent { get; set; } = null!;

    public int? TenantId { get; set; }
}