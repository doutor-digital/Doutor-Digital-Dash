namespace LeadAnalytics.Api.Models;

public class LeadEvent
{
    public string ExternalId { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Stage { get; set; } = string.Empty;
    public string AttendantId { get; set; } = string.Empty;
    public string SourceSystem { get; set; } = string.Empty;
}
