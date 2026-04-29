namespace LeadAnalytics.Api.DTOs.Insights;

public class HeatmapDto
{
    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }
    public int TotalLeads { get; set; }
    public int Max { get; set; }
    public List<HeatmapCellDto> Cells { get; set; } = new();
    public List<HeatmapWeekdaySummaryDto> ByWeekday { get; set; } = new();
    public List<HeatmapHourSummaryDto> ByHour { get; set; } = new();
}

public class HeatmapCellDto
{
    public int Weekday { get; set; }
    public int Hour { get; set; }
    public int Count { get; set; }
}

public class HeatmapWeekdaySummaryDto
{
    public int Weekday { get; set; }
    public string Label { get; set; } = "";
    public int Count { get; set; }
}

public class HeatmapHourSummaryDto
{
    public int Hour { get; set; }
    public int Count { get; set; }
}
