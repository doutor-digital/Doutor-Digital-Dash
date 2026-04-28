namespace LeadAnalytics.Api.DTOs.Response;

public class MarkAttendanceDto
{
    public bool Attended { get; set; }

    // Obrigatório quando Attended=true. Valores: "fechou" | "nao_fechou".
    public string? Outcome { get; set; }

    public string? Notes { get; set; }
}
