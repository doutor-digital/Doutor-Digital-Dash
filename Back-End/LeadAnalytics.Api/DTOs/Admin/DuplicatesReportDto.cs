namespace LeadAnalytics.Api.DTOs.Admin;

public class DuplicatesReportDto
{
    public bool DryRun { get; set; }
    public int GroupsFound { get; set; }
    public int ContactsToDelete { get; set; }
    public List<DuplicateGroupDto> Groups { get; set; } = [];
}

public class DuplicateGroupDto
{
    public int TenantId { get; set; }
    public string PhoneNormalized { get; set; } = string.Empty;
    public int Count { get; set; }
    public int KeepContactId { get; set; }
    public string KeepName { get; set; } = string.Empty;
    public DateTime KeepCreatedAt { get; set; }
    public List<int> DeleteContactIds { get; set; } = [];
}

public class DuplicatesDeleteSummaryDto
{
    public int GroupsFound { get; set; }
    public int ContactsDeleted { get; set; }
    public int Batches { get; set; }
    public long DurationMs { get; set; }
}
