namespace LeadAnalytics.Api.DTOs.Response;

public class OvernightLeadsDto
{
    public int Total { get; set; }
    public int? UnitId { get; set; }
    public int? ClinicId { get; set; }
    public string UnitName { get; set; } = string.Empty;

    public DateTime PeriodStartLocal { get; set; }
    public DateTime PeriodEndLocal { get; set; }

    public int StartHour { get; set; }
    public int EndHour { get; set; }

    public List<OvernightLeadItemDto> Leads { get; set; } = new();
    public List<OvernightHourBucketDto> HourBreakdown { get; set; } = new();
    public List<OvernightSourceBucketDto> SourceBreakdown { get; set; } = new();
}

public class OvernightLeadItemDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string Source { get; set; } = "DESCONHECIDO";
    public string Channel { get; set; } = "DESCONHECIDO";
    public string CurrentStage { get; set; } = "SEM_ETAPA";
    public string? ConversationState { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime CreatedAtLocal { get; set; }
}

public class OvernightHourBucketDto
{
    public int Hour { get; set; }
    public int Count { get; set; }
}

public class OvernightSourceBucketDto
{
    public string Source { get; set; } = "DESCONHECIDO";
    public int Count { get; set; }
}
