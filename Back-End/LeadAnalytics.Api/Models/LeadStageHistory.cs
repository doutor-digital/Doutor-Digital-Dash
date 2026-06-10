namespace LeadAnalytics.Api.Models;

public class LeadStageHistory
{
    public int Id { get; set; }
    public int LeadId { get; set; }
    public Lead Lead { get; set; } = null!;
    public int StageId { get; set; }
    public string StageLabel { get; set; } = null!;
    public DateTime ChangedAt { get; set; }
}