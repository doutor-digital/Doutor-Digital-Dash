namespace LeadAnalytics.Api.DTOs.Insights;

public class CohortDto
{
    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }
    public string Granularity { get; set; } = "week";
    public List<int> Days { get; set; } = new();
    public List<CohortRowDto> Rows { get; set; } = new();
}

public class CohortRowDto
{
    public DateTime CohortStart { get; set; }
    public string Label { get; set; } = "";
    public int Size { get; set; }
    public List<CohortCellDto> Cells { get; set; } = new();
}

public class CohortCellDto
{
    public int DaysSinceCohort { get; set; }
    public int Converted { get; set; }
    public double Rate { get; set; }
}
