namespace LeadAnalytics.Api.DTOs.Timeline;

public class LeadTimelineDto
{
    public LeadHeaderDto Lead { get; set; } = new();
    public AttributionDto? Attribution { get; set; }
    public List<StageStepDto> Stages { get; set; } = [];
    public List<AssignmentStepDto> Assignments { get; set; } = [];
    public List<ConversationDto> Conversations { get; set; } = [];
    public List<InteractionDto> Interactions { get; set; } = [];
    public TimelineInsightsDto Insights { get; set; } = new();
}

public class LeadHeaderDto
{
    public int Id { get; set; }
    public int ExternalId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string Channel { get; set; } = string.Empty;
    public string CurrentStage { get; set; } = string.Empty;
    public string? ConversationState { get; set; }
    public bool HasAppointment { get; set; }
    public bool HasPayment { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ConvertedAt { get; set; }
}

public class AttributionDto
{
    public string Phone { get; set; } = string.Empty;
    public string CtwaClid { get; set; } = string.Empty;
    public string? SourceId { get; set; }
    public string? SourceType { get; set; }
    public string MatchType { get; set; } = string.Empty;
    public string Confidence { get; set; } = string.Empty;
    public DateTime MatchedAt { get; set; }
}

public class StageStepDto
{
    public string Label { get; set; } = string.Empty;
    public int StageId { get; set; }
    public DateTime EnteredAt { get; set; }
    public DateTime? ExitedAt { get; set; }
    public double? DurationMinutes { get; set; }
    public bool IsCurrent { get; set; }
}

public class AssignmentStepDto
{
    public int AttendantId { get; set; }
    public string AttendantName { get; set; } = string.Empty;
    public string? StageAtAssignment { get; set; }
    public DateTime AssignedAt { get; set; }
    public double? MinutesUntilFirstReply { get; set; }
}

public class ConversationDto
{
    public int Id { get; set; }
    public string Channel { get; set; } = string.Empty;
    public string? Source { get; set; }
    public string ConversationState { get; set; } = string.Empty;
    public string? AttendantName { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public double? DurationMinutes { get; set; }
    public int InteractionsCount { get; set; }
}

public class InteractionDto
{
    public int Id { get; set; }
    public int ConversationId { get; set; }
    public string Type { get; set; } = string.Empty;
    public string? Content { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class TimelineInsightsDto
{
    public double? TotalMinutesUntilConversion { get; set; }
    public double? MinutesUntilFirstAssignment { get; set; }
    public double MinutesInBot { get; set; }
    public double MinutesInQueue { get; set; }
    public double MinutesInService { get; set; }
    public int StageChanges { get; set; }
    public int Reassignments { get; set; }
    public string? LongestStageLabel { get; set; }
    public double? LongestStageMinutes { get; set; }
}
