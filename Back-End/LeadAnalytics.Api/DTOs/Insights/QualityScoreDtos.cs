namespace LeadAnalytics.Api.DTOs.Insights;

public class QualityScoreDto
{
    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }
    public int TotalLeads { get; set; }
    public int TotalConversions { get; set; }
    public List<QualityScoreItemDto> Sources { get; set; } = new();
}

public class QualityScoreItemDto
{
    public string Source { get; set; } = "";
    public int Leads { get; set; }
    public int Conversions { get; set; }
    public double ConversionRate { get; set; }
    public double QualityScore { get; set; }
    public string Tier { get; set; } = "C";
    public double AvgTimeToConvertHours { get; set; }
    public double ResponseRate { get; set; }
}
