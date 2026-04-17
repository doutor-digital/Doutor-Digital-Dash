namespace LeadAnalytics.Api.DTOs.Response;

/// <summary>
/// Resumo de métricas agregadas de uma clínica
/// </summary>
public class ClinicSummaryDto
{
    /// <summary>
    /// ID da clínica
    /// </summary>
    public int ClinicId { get; set; }

    /// <summary>
    /// Nome da clínica
    /// </summary>
    public string ClinicName { get; set; } = string.Empty;

    /// <summary>
    /// Período analisado - início
    /// </summary>
    public DateTime PeriodStart { get; set; }

    /// <summary>
    /// Período analisado - fim
    /// </summary>
    public DateTime PeriodEnd { get; set; }

    /// <summary>
    /// Total de leads no período
    /// </summary>
    public int TotalLeads { get; set; }

    /// <summary>
    /// Leads atualmente em BOT
    /// </summary>
    public int LeadsInBot { get; set; }

    /// <summary>
    /// Leads atualmente em QUEUE
    /// </summary>
    public int LeadsInQueue { get; set; }

    /// <summary>
    /// Leads atualmente em SERVICE
    /// </summary>
    public int LeadsInService { get; set; }

    /// <summary>
    /// Leads concluídos
    /// </summary>
    public int LeadsConcluded { get; set; }

    /// <summary>
    /// Tempo médio de resposta (minutos)
    /// Tempo do lead ser criado até atendente assumir
    /// </summary>
    public double? AverageTimeToFirstResponseMinutes { get; set; }

    /// <summary>
    /// Tempo médio de resolução (minutos)
    /// Tempo do lead ser criado até conclusão
    /// </summary>
    public double? AverageTimeToResolutionMinutes { get; set; }

    /// <summary>
    /// Tempo médio em BOT (minutos)
    /// </summary>
    public double? AverageTimeInBotMinutes { get; set; }

    /// <summary>
    /// Tempo médio em QUEUE (minutos)
    /// </summary>
    public double? AverageTimeInQueueMinutes { get; set; }

    /// <summary>
    /// Tempo médio em SERVICE (minutos)
    /// </summary>
    public double? AverageTimeInServiceMinutes { get; set; }

    /// <summary>
    /// Leads demorando muito (alertas)
    /// </summary>
    public int DelayedLeadsCount { get; set; }

    /// <summary>
    /// Performance por atendente
    /// </summary>
    public List<AttendantPerformanceDto> AttendantsPerformance { get; set; } = new();

    /// <summary>
    /// Distribuição de leads por estado (para gráficos)
    /// </summary>
    public Dictionary<string, int> LeadsByState { get; set; } = new();

    /// <summary>
    /// Últimas atualizações
    /// </summary>
    public DateTime LastCalculatedAt { get; set; }
}

/// <summary>
/// Performance de um atendente
/// </summary>
public class AttendantPerformanceDto
{
    /// <summary>
    /// ID do atendente
    /// </summary>
    public int AttendantId { get; set; }

    /// <summary>
    /// Nome do atendente
    /// </summary>
    public string AttendantName { get; set; } = string.Empty;

    /// <summary>
    /// Total de leads atendidos
    /// </summary>
    public int TotalLeadsHandled { get; set; }

    /// <summary>
    /// Leads atualmente em atendimento
    /// </summary>
    public int CurrentActiveLeads { get; set; }

    /// <summary>
    /// Leads concluídos
    /// </summary>
    public int LeadsConcluded { get; set; }

    /// <summary>
    /// Tempo médio de atendimento (minutos)
    /// </summary>
    public double? AverageServiceTimeMinutes { get; set; }

    /// <summary>
    /// Tempo médio até conclusão (minutos)
    /// </summary>
    public double? AverageResolutionTimeMinutes { get; set; }

    /// <summary>
    /// Taxa de conversão (concluídos / total)
    /// </summary>
    public double? ConversionRate { get; set; }
}