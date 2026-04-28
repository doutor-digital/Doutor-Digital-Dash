namespace LeadAnalytics.Api.DTOs.Response;

public class RecoveryLeadDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Phone { get; set; }
    public int? UnitId { get; set; }
    public string? UnitName { get; set; }
    public string? Source { get; set; }
    public string? Campaign { get; set; }
    public DateTime? AttendanceStatusAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
