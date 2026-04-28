namespace LeadAnalytics.Api.DTOs.Response;

public class LeadDetailDto
{
    public int Id { get; set; }
    public int ExternalId { get; set; }
    public int TenantId { get; set; }

    public string Name { get; set; } = null!;
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Cpf { get; set; }
    public string? Gender { get; set; }

    public string Source { get; set; } = null!;
    public string Channel { get; set; } = null!;
    public string Campaign { get; set; } = null!;
    public string? Ad { get; set; }
    public string TrackingConfidence { get; set; } = null!;

    public string CurrentStage { get; set; } = null!;
    public int? CurrentStageId { get; set; }
    public string Status { get; set; } = null!;
    public string? ConversationState { get; set; }

    public bool HasAppointment { get; set; }
    public bool HasPayment { get; set; }
    public bool? HasHealthInsurancePlan { get; set; }
    public string? Observations { get; set; }
    public List<string> Tags { get; set; } = new();

    // "compareceu" | "faltou" | "aguardando" | null
    public string? AttendanceStatus { get; set; }
    public DateTime? AttendanceStatusAt { get; set; }

    public int? UnitId { get; set; }
    public string? UnitName { get; set; }

    public int? AttendantId { get; set; }
    public string? AttendantName { get; set; }
    public string? AttendantEmail { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? ConvertedAt { get; set; }

    public List<LeadStageHistoryDto> StageHistory { get; set; } = new();
    public List<LeadConversationDto> Conversations { get; set; } = new();
    public List<LeadAssignmentDto> Assignments { get; set; } = new();
    public List<LeadPaymentDto> Payments { get; set; } = new();
}

public class LeadStageHistoryDto
{
    public int Id { get; set; }
    public int StageId { get; set; }
    public string StageLabel { get; set; } = null!;
    public DateTime ChangedAt { get; set; }
}

public class LeadConversationDto
{
    public int Id { get; set; }
    public string Channel { get; set; } = null!;
    public string? Source { get; set; }
    public string ConversationState { get; set; } = null!;
    public DateTime StartedAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public int? AttendantId { get; set; }
    public string? AttendantName { get; set; }
    public List<LeadInteractionDto> Interactions { get; set; } = new();
}

public class LeadInteractionDto
{
    public int Id { get; set; }
    public string Type { get; set; } = null!;
    public string? Content { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class LeadAssignmentDto
{
    public int Id { get; set; }
    public int AttendantId { get; set; }
    public string? AttendantName { get; set; }
    public string? Stage { get; set; }
    public DateTime AssignedAt { get; set; }
}

public class LeadPaymentDto
{
    public int Id { get; set; }
    public decimal Amount { get; set; }
    public DateTime PaidAt { get; set; }
}
