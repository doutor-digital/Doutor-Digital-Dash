namespace LeadAnalytics.Api.DTOs.Alerts;

/// <summary>
/// Parcela que passou de "pendente" para "atrasado" na última execução.
/// Carrega tenant/unit/lead para o n8n rotear a notificação por cliente.
/// </summary>
public class OverdueInstallmentDto
{
    public int InstallmentId { get; set; }
    public int TreatmentId { get; set; }
    public int LeadId { get; set; }
    public string? LeadName { get; set; }
    public string? LeadPhone { get; set; }
    public int TenantId { get; set; }
    public int? UnitId { get; set; }
    public int Sequence { get; set; }
    public decimal Amount { get; set; }
    public string PaymentMethod { get; set; } = "pix";
    public DateOnly? DueDate { get; set; }
    public int DaysOverdue { get; set; }
}
