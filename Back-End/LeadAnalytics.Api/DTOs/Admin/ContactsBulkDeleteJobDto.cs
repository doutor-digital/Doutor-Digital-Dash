using System.Text.Json.Serialization;
using LeadAnalytics.Api.DTOs.Response;

namespace LeadAnalytics.Api.DTOs.Admin;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ContactsBulkDeleteMode
{
    Ids = 0,
    Filter = 1,
}

public class ContactsBulkDeleteSelection
{
    public ContactsBulkDeleteMode Mode { get; set; }
    public List<int>? Ids { get; set; }
    public ContactFiltersDto? Filters { get; set; }
}

public class StartContactsBulkDeleteRequest
{
    public int TenantId { get; set; }
    public ContactsBulkDeleteSelection Selection { get; set; } = new();
    public int? BatchSize { get; set; }
}

public class StartContactsBulkDeleteResponse
{
    public string JobId { get; set; } = string.Empty;
    public DuplicateDeleteJobStatus Status { get; set; }
    public int EstimatedTotal { get; set; }
}

public class ContactsBulkDeleteJobDto
{
    public string Id { get; set; } = string.Empty;
    public DuplicateDeleteJobStatus Status { get; set; }

    public int TenantId { get; set; }
    public ContactsBulkDeleteMode Mode { get; set; }
    public int BatchSize { get; set; }

    public int ContactsToDeleteTotal { get; set; }
    public int ContactsDeleted { get; set; }
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
