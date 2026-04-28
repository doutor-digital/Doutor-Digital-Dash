namespace LeadAnalytics.Api.DTOs.Response;

public class StageChangeDto
{
    public int Id { get; set; }
    public int LeadId { get; set; }
    public string LeadName { get; set; } = "";
    public string? LeadPhone { get; set; }
    public int? UnitId { get; set; }
    public string? UnitName { get; set; }
    public string? Source { get; set; }
    public string? FromStage { get; set; }
    public string ToStage { get; set; } = "";
    public DateTime ChangedAt { get; set; }
}

public class StageChangesSummaryDto
{
    public int Total { get; set; }
    public List<StageChangeDailyPointDto> Daily { get; set; } = new();
    public List<StageChangeDestinationDto> ByDestination { get; set; } = new();
    public List<StageChangeDto> Items { get; set; } = new();
}

public class StageChangeDailyPointDto
{
    public DateTime Date { get; set; }
    public int Count { get; set; }
}

public class StageChangeDestinationDto
{
    public string Stage { get; set; } = "";
    public int Count { get; set; }
}
