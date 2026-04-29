namespace LeadAnalytics.Api.DTOs.Insights;

public class SlaResponseDto
{
    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }
    public int TotalLeads { get; set; }
    public int LeadsWithFirstResponse { get; set; }
    public double AverageFirstResponseMinutes { get; set; }
    public double MedianFirstResponseMinutes { get; set; }
    public double P90FirstResponseMinutes { get; set; }
    public int WithinTargetCount { get; set; }
    public int OutsideTargetCount { get; set; }
    public int TargetMinutes { get; set; }
    public List<SlaByAttendantDto> ByAttendant { get; set; } = new();
    public List<SlaBucketDto> Buckets { get; set; } = new();
}

public class SlaByAttendantDto
{
    public int AttendantId { get; set; }
    public string AttendantName { get; set; } = "";
    public int TotalLeads { get; set; }
    public double AverageMinutes { get; set; }
    public double MedianMinutes { get; set; }
    public int WithinTarget { get; set; }
    public double WithinTargetRate { get; set; }
}

public class SlaBucketDto
{
    public string Range { get; set; } = "";
    public int Count { get; set; }
    public double Percent { get; set; }
}
