namespace LeadAnalytics.Api.DTOs.Response;

/// <summary>
/// Contagem de leads em atendimento
/// </summary>
public class LeadsInServiceDto
{
    /// <summary>
    /// Leads sendo atendidos (service)
    /// </summary>
    public int InService { get; set; }

    /// <summary>
    /// Leads na fila aguardando (queue)
    /// </summary>
    public int InQueue { get; set; }

    /// <summary>
    /// Leads com o bot (bot)
    /// </summary>
    public int InBot { get; set; }

    /// <summary>
    /// Leads concluídos (concluido)
    /// </summary>
    public int Concluded { get; set; }

    /// <summary>
    /// Total de leads ativos (service + queue + bot)
    /// </summary>
    public int TotalActive { get; set; }
}