namespace LeadAnalytics.Api.DTOs.Insights;

public class LostReasonsDto
{
    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }
    public int TotalLost { get; set; }
    public int TotalAnalyzed { get; set; }
    public List<LostReasonItemDto> Reasons { get; set; } = new();
    public List<LostReasonByStageDto> ByStage { get; set; } = new();
}

public class LostReasonItemDto
{
    public string Reason { get; set; } = "";
    public string Category { get; set; } = "";
    public int Count { get; set; }
    public double Percent { get; set; }
    public List<string> Keywords { get; set; } = new();
}

public class LostReasonByStageDto
{
    public string Stage { get; set; } = "";
    public int Count { get; set; }
    public string TopReason { get; set; } = "";
}
