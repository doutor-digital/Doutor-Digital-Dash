namespace LeadAnalytics.Api.DTOs.Response;

public class FiltroLeadsPeriodoDto
{
    public int ClinicId { get; set; }          // obrigatório
    public int Ano { get; set; }               // obrigatório
    public int? Mes { get; set; }              // opcional
    public double Semana { get; set; }           // opcional (ISO)
    public int? Dia { get; set; }              // opcional
}
