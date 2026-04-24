using System.Text.Json.Serialization;

namespace LeadAnalytics.Api.DTOs.Admin;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DuplicateDeleteJobStatus
{
    Queued = 0,
    Running = 1,
    Completed = 2,
    Failed = 3,
    Cancelling = 4,
    Cancelled = 5,
}

public class DuplicateDeleteJobDto
{
    public string Id { get; set; } = string.Empty;
    public DuplicateDeleteJobStatus Status { get; set; }

    public int? TenantId { get; set; }
    public bool IgnoreTenant { get; set; }
    public int BatchSize { get; set; }

    public int ContactsToDeleteTotal { get; set; }
    public int ContactsDeleted { get; set; }
    public int GroupsFound { get; set; }
    public int BatchesExecuted { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }

    public string? Error { get; set; }
    public string CreatedBy { get; set; } = string.Empty;

    public int ProgressPct =>
        ContactsToDeleteTotal <= 0
            ? 0
            : (int)Math.Min(100, Math.Round(ContactsDeleted * 100.0 / ContactsToDeleteTotal));
}

public class StartDuplicateDeleteJobRequest
{
    public int? TenantId { get; set; }
    public bool IgnoreTenant { get; set; }
    public int? BatchSize { get; set; }
}

public class StartDuplicateDeleteJobResponse
{
    public string JobId { get; set; } = string.Empty;
    public DuplicateDeleteJobStatus Status { get; set; }
}
