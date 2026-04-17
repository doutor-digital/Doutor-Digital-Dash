namespace LeadAnalytics.Api.DTOs.Filter;

public class FiltroLeadsPeriodoDto
{
    public int ClinicId { get; set; }
    public int Ano { get; set; }
    public int? Mes { get; set; }
    public int? Dia { get; set; }
}