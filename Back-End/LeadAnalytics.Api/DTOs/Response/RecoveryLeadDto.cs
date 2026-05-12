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
    public int? AttendantId { get; set; }
    public string? AttendantName { get; set; }
    public DateTime? AttendanceStatusAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public int AttemptsCount { get; set; }
    public DateTime? LastAttemptAt { get; set; }
    public string? LastAttemptOutcome { get; set; }
}

public class RecoveryAttemptDto
{
    public int Id { get; set; }
    public int LeadId { get; set; }
    public string Method { get; set; } = "whatsapp";
    public string Outcome { get; set; } = "no_answer";
    public string? Notes { get; set; }
    public int? AttendantId { get; set; }
    public string? AttendantName { get; set; }
    public int? CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class CreateRecoveryAttemptDto
{
    public string Method { get; set; } = "whatsapp";
    public string Outcome { get; set; } = "no_answer";
    public string? Notes { get; set; }
}

public class MarkRecoveredDto
{
    public string? Notes { get; set; }
}
