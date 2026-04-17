namespace LeadAnalytics.Api.DTOs.Response;

/// <summary>
/// Métricas de tempo de um lead específico
/// </summary>
public class LeadMetricsDto
{
    /// <summary>
    /// ID do lead
    /// </summary>
    public int LeadId { get; set; }

    /// <summary>
    /// ID externo (Cloudia)
    /// </summary>
    public int ExternalId { get; set; }

    /// <summary>
    /// Nome do lead
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Telefone
    /// </summary>
    public string? Phone { get; set; }

    /// <summary>
    /// Estado atual da conversa
    /// </summary>
    public string CurrentState { get; set; } = string.Empty;

    /// <summary>
    /// Quando o lead foi criado
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Última atualização
    /// </summary>
    public DateTime LastUpdatedAt { get; set; }

    /// <summary>
    /// Tempo total em minutos no estado BOT
    /// </summary>
    public double? TimeInBotMinutes { get; set; }

    /// <summary>
    /// Tempo total em minutos no estado QUEUE
    /// </summary>
    public double? TimeInQueueMinutes { get; set; }

    /// <summary>
    /// Tempo total em minutos no estado SERVICE
    /// </summary>
    public double? TimeInServiceMinutes { get; set; }

    /// <summary>
    /// Tempo total em minutos no estado CONCLUIDO
    /// </summary>
    public double? TimeInConcluidoMinutes { get; set; }

    /// <summary>
    /// Tempo total de resposta (criação até primeiro atendimento humano)
    /// Em minutos
    /// </summary>
    public double? TimeToFirstResponseMinutes { get; set; }

    /// <summary>
    /// Tempo total de resolução (criação até conclusão)
    /// Em minutos
    /// </summary>
    public double? TimeToResolutionMinutes { get; set; }

    /// <summary>
    /// Número total de transições de estado
    /// </summary>
    public int TotalTransitions { get; set; }

    /// <summary>
    /// Atendente atualmente responsável (se houver)
    /// </summary>
    public int? CurrentAttendantId { get; set; }

    /// <summary>
    /// Nome do atendente atual
    /// </summary>
    public string? CurrentAttendantName { get; set; }

    /// <summary>
    /// Está demorando muito? (alerta)
    /// </summary>
    public bool IsDelayed { get; set; }

    /// <summary>
    /// Razão do alerta (se aplicável)
    /// </summary>
    public string? DelayReason { get; set; }

    /// <summary>
    /// Timeline de todas as conversas/estados
    /// </summary>
    public List<ConversationPeriodDto> Timeline { get; set; } = new();
}

/// <summary>
/// Período de uma conversa em um estado específico
/// </summary>
public class ConversationPeriodDto
{
    /// <summary>
    /// ID da conversa
    /// </summary>
    public int ConversationId { get; set; }

    /// <summary>
    /// Estado da conversa neste período
    /// </summary>
    public string State { get; set; } = string.Empty;

    /// <summary>
    /// Quando começou neste estado
    /// </summary>
    public DateTime StartedAt { get; set; }

    /// <summary>
    /// Quando terminou neste estado (null se ainda ativo)
    /// </summary>
    public DateTime? EndedAt { get; set; }

    /// <summary>
    /// Duração em minutos (null se ainda ativo)
    /// </summary>
    public double? DurationMinutes { get; set; }

    /// <summary>
    /// Atendente responsável (se houver)
    /// </summary>
    public int? AttendantId { get; set; }

    /// <summary>
    /// Nome do atendente
    /// </summary>
    public string? AttendantName { get; set; }

    /// <summary>
    /// Está ativo? (EndedAt == null)
    /// </summary>
    public bool IsActive { get; set; }
}