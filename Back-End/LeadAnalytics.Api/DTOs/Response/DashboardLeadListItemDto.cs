namespace LeadAnalytics.Api.DTOs.Response;

/// <summary>
/// Item de lista usada nos drill-downs do dashboard
/// (/dashboard/scheduled, /dashboard/attended).
/// </summary>
public class DashboardLeadListItemDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Phone { get; set; }
    public int? UnitId { get; set; }
    public string? UnitName { get; set; }
    public int? AttendantId { get; set; }
    public string? AttendantName { get; set; }
    public string? Source { get; set; }
    public string? Campaign { get; set; }
    public string CurrentStage { get; set; } = "";
    public string? AttendanceStatus { get; set; }
    public DateTime? AppointmentScheduledAt { get; set; }
    public DateTime? AttendanceStatusAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public decimal? ConsultationValue { get; set; }
    public bool? ClosedTreatment { get; set; }
}
