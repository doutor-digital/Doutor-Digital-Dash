namespace LeadAnalytics.Api.DTOs.Alerts;

/// <summary>
/// Tratamento em "aguardando_dados" há mais que o limite (default 24h) sem
/// preenchimento da SDR. Detecção read-only — a notificação é responsabilidade
/// do n8n.
/// </summary>
public class PendingFillDto
{
    public int TreatmentId { get; set; }
    public int LeadId { get; set; }
    public string? LeadName { get; set; }
    public string? LeadPhone { get; set; }
    public int TenantId { get; set; }
    public int? UnitId { get; set; }
    public DateTime CreatedAt { get; set; }
    public double HoursPending { get; set; }
}
