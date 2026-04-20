namespace LeadAnalytics.Api.Models;

public class PaymentSplit
{
    public int Id { get; set; }

    public int PaymentId { get; set; }
    public Payment Payment { get; set; } = null!;

    public string PaymentMethod { get; set; } = null!;

    public decimal Amount { get; set; }

    public int Installments { get; set; } = 1;
    public decimal InstallmentValue { get; set; }

    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; }
}
