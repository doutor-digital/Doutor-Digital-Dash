namespace LeadAnalytics.Api.DTOs.Insights;

public class ForecastDto
{
    public DateTime AsOf { get; set; }
    public int HorizonDays { get; set; }
    public int OpenLeadsTotal { get; set; }
    public double ProjectedConversions { get; set; }
    public decimal ProjectedRevenue { get; set; }
    public double OverallConversionRate { get; set; }
    public List<ForecastByStageDto> ByStage { get; set; } = new();
    public List<ForecastTimelineDto> Timeline { get; set; } = new();
}

public class ForecastByStageDto
{
    public string Stage { get; set; } = "";
    public int OpenLeads { get; set; }
    public double HistoricalConversionRate { get; set; }
    public double ProjectedConversions { get; set; }
    public decimal ProjectedRevenue { get; set; }
}

public class ForecastTimelineDto
{
    public DateTime Date { get; set; }
    public double Projected { get; set; }
    public double LowerBound { get; set; }
    public double UpperBound { get; set; }
}
