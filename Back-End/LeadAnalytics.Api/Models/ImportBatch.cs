namespace LeadAnalytics.Api.Models;

public class ImportBatch
{
    public int Id { get; set; }
    public int TenantId { get; set; }
    public string Filename { get; set; } = string.Empty;
    public int? UploadedByUserId { get; set; }

    // "processing" | "done" | "failed"
    public string Status { get; set; } = "processing";

    public int TotalRows { get; set; }
    public int CreatedCount { get; set; }
    public int UpdatedCount { get; set; }
    public int SkippedCount { get; set; }
    public int ErrorCount { get; set; }

    public string? ErrorsJson { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
}
