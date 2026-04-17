namespace LeadAnalytics.Api.DTOs.Response;

public class DailyRelatoryDto
{
    public string Unidade { get; set; } = string.Empty;
    public int TotalLeads { get; set; }
    public int Agendamentos { get; set; }
    public int ComPagamento { get; set; }
    public int Resgastes { get; set; }
    public string Observacoes { get; set; } = string.Empty;
    public List<string> Atendentes { get; set; } = [];
}