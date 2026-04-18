namespace LeadAnalytics.Api.DTOs.Response;

public class AttendantRankingDto
{
    public int AttendantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Email { get; set; }

    public int Total { get; set; }
    public int Agendado { get; set; }
    public int Pago { get; set; }
    public int Tratamento { get; set; }
    public int Conversions { get; set; }
    public int Active { get; set; }

    public double AgendadoRate { get; set; }
    public double PagoRate { get; set; }
    public double ConversionRate { get; set; }

    public DateTime? FirstAssignedAt { get; set; }
    public DateTime? LastAssignedAt { get; set; }
}
