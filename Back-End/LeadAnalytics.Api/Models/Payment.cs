namespace LeadAnalytics.Api.Models;

public class Payment
{
    public int Id { get; set; }
    public int LeadId { get; set; }
    public Lead Lead { get; set; } = null!;
    public decimal Amount { get; set; }
    public DateTime PaidAt { get; set; }
}