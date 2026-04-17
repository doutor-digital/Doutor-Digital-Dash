using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.DTOs.Response;
using LeadAnalytics.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace LeadAnalytics.Api.Service;

/// <summary>
/// Serviço para cálculo de métricas e analytics de leads
/// Calcula tempos por estado, performance de atendentes e alertas
/// </summary>
public class LeadAnalyticsService(AppDbContext context, ILogger<LeadAnalyticsService> logger)
{
    private readonly AppDbContext _context = context;
    private readonly ILogger<LeadAnalyticsService> _logger = logger;

    // ═══════════════════════════════════════════════════════════════
    // CONSTANTES - LIMITES DE TEMPO PARA ALERTAS
    // ═══════════════════════════════════════════════════════════════
    
    private const int ALERTA_BOT_MINUTOS = 30;
    private const int ALERTA_QUEUE_MINUTOS = 15;
    private const int ALERTA_SERVICE_MINUTOS = 120;

    // ═══════════════════════════════════════════════════════════════
    // MÉTRICAS INDIVIDUAIS DE LEADS
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Obter todas as métricas de um lead específico
    /// Inclui: tempos por estado, timeline, alertas
    /// </summary>
    public async Task<LeadMetricsDto?> GetLeadMetricsAsync(int leadId)
    {
        // ─────────────────────────────────────────────────────────
        // 1. BUSCAR LEAD COM RELACIONAMENTOS
        // ─────────────────────────────────────────────────────────
        
        var lead = await _context.Leads
            .Include(l => l.Unit)
            .Include(l => l.Attendant)
            .FirstOrDefaultAsync(l => l.Id == leadId);

        if (lead == null)
        {
            _logger.LogWarning("Lead {LeadId} não encontrado", leadId);
            return null;
        }

        // ─────────────────────────────────────────────────────────
        // 2. BUSCAR HISTÓRICO DE CONVERSAS (PERÍODOS DE ESTADO)
        // ─────────────────────────────────────────────────────────
        
        var conversations = await _context.LeadConversations
            .Where(lc => lc.LeadId == leadId)
            .OrderBy(lc => lc.StartedAt)
            .ToListAsync();

        // ─────────────────────────────────────────────────────────
        // 3. CALCULAR TEMPO EM CADA ESTADO
        // ─────────────────────────────────────────────────────────
        
        var timeInBot = CalcularTempoNoEstado(conversations, "bot");
        var timeInQueue = CalcularTempoNoEstado(conversations, "queue");
        var timeInService = CalcularTempoNoEstado(conversations, "service");
        var timeInConcluido = CalcularTempoNoEstado(conversations, "concluido");

        // ─────────────────────────────────────────────────────────
        // 4. CALCULAR TEMPO ATÉ PRIMEIRO ATENDIMENTO
        // ─────────────────────────────────────────────────────────
        
        var timeToFirstResponse = CalcularTempoAteAtendimento(lead, conversations);

        // ─────────────────────────────────────────────────────────
        // 5. CALCULAR TEMPO ATÉ RESOLUÇÃO (CONCLUSÃO)
        // ─────────────────────────────────────────────────────────
        
        var timeToResolution = CalcularTempoAteResolucao(lead, conversations);

        // ─────────────────────────────────────────────────────────
        // 6. VERIFICAR ALERTAS (DEMORANDO MUITO?)
        // ─────────────────────────────────────────────────────────
        
        var (isDelayed, delayReason) = VerificarAlertas(lead, conversations);

        // ─────────────────────────────────────────────────────────
        // 7. MONTAR TIMELINE DE CONVERSAS
        // ─────────────────────────────────────────────────────────
        
        var timeline = MontarTimeline(conversations);

        // ─────────────────────────────────────────────────────────
        // 8. RETORNAR DTO COMPLETO
        // ─────────────────────────────────────────────────────────
        
        return new LeadMetricsDto
        {
            LeadId = lead.Id,
            ExternalId = lead.ExternalId,
            Name = lead.Name,
            Phone = lead.Phone,
            CurrentState = lead.ConversationState ?? "desconhecido",
            CreatedAt = lead.CreatedAt,
            LastUpdatedAt = lead.UpdatedAt, // ✅ CORRIGIDO: UpdatedAt, não LastUpdatedAt
            TimeInBotMinutes = timeInBot > 0 ? timeInBot : null,
            TimeInQueueMinutes = timeInQueue > 0 ? timeInQueue : null,
            TimeInServiceMinutes = timeInService > 0 ? timeInService : null,
            TimeInConcluidoMinutes = timeInConcluido > 0 ? timeInConcluido : null,
            TimeToFirstResponseMinutes = timeToFirstResponse,
            TimeToResolutionMinutes = timeToResolution,
            TotalTransitions = conversations.Count,
            CurrentAttendantId = lead.AttendantId,
            CurrentAttendantName = lead.Attendant?.Name,
            IsDelayed = isDelayed,
            DelayReason = delayReason,
            Timeline = timeline
        };
    }

    /// <summary>
    /// Obter métricas de múltiplos leads com filtros
    /// </summary>
    public async Task<List<LeadMetricsDto>> GetLeadsMetricsAsync(
        int unitId,
        DateTime? startDate = null,
        DateTime? endDate = null,
        string? state = null)
    {
        // ─────────────────────────────────────────────────────────
        // BUSCAR LEADS COM FILTROS
        // ─────────────────────────────────────────────────────────
        
        var query = _context.Leads
            .Include(l => l.Unit)      // ✅ Unit é navegação
            .Include(l => l.Attendant) // ✅ Attendant é navegação
            .Where(l => l.UnitId == unitId); // ✅ CORRIGIDO: UnitId ao invés de TenantId

        // Filtro de data inicial
        if (startDate.HasValue)
        {
            query = query.Where(l => l.CreatedAt >= startDate.Value);
        }

        // Filtro de data final
        if (endDate.HasValue)
        {
            query = query.Where(l => l.CreatedAt <= endDate.Value);
        }

        // Filtro de estado
        if (!string.IsNullOrEmpty(state))
        {
            query = query.Where(l => l.ConversationState == state);
        }

        var leads = await query.ToListAsync();

        // ─────────────────────────────────────────────────────────
        // CALCULAR MÉTRICAS DE CADA LEAD
        // ─────────────────────────────────────────────────────────
        
        var metrics = new List<LeadMetricsDto>();

        foreach (var lead in leads)
        {
            var leadMetrics = await GetLeadMetricsAsync(lead.Id);
            
            if (leadMetrics != null)
            {
                metrics.Add(leadMetrics);
            }
        }

        return metrics;
    }

    // ═══════════════════════════════════════════════════════════════
    // RESUMO AGREGADO DA CLÍNICA
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Obter resumo com médias e totais da clínica
    /// </summary>
    public async Task<ClinicSummaryDto> GetClinicSummaryAsync(
        int unitId,
        DateTime? startDate = null,
        DateTime? endDate = null)
    {
        // ─────────────────────────────────────────────────────────
        // 1. DEFINIR PERÍODO (PADRÃO: ÚLTIMOS 30 DIAS)
        // ─────────────────────────────────────────────────────────
        
        var start = startDate ?? DateTime.UtcNow.Date.AddDays(-30);
        var end = endDate ?? DateTime.UtcNow;

        // ─────────────────────────────────────────────────────────
        // 2. BUSCAR UNIDADE (OPCIONAL - USA NOME GENÉRICO SE NÃO EXISTIR)
        // ─────────────────────────────────────────────────────────
        
        var unit = await _context.Units.FindAsync(unitId);
        var unitName = unit?.Name ?? $"Unidade {unitId}";

        // ─────────────────────────────────────────────────────────
        // 3. CONTAR TOTAL DE LEADS NO PERÍODO
        // ─────────────────────────────────────────────────────────
        
        var leadsQuery = _context.Leads
            .Where(l => l.UnitId == unitId 
                && l.CreatedAt >= start 
                && l.CreatedAt <= end);

        var totalLeads = await leadsQuery.CountAsync();

        // ─────────────────────────────────────────────────────────
        // 4. DISTRIBUIÇÃO POR ESTADO
        // ─────────────────────────────────────────────────────────
        
        var leadsByState = await leadsQuery
            .Where(l => l.ConversationState != null)
            .GroupBy(l => l.ConversationState)
            .Select(g => new { State = g.Key!, Count = g.Count() })
            .ToDictionaryAsync(x => x.State, x => x.Count);

        var leadsInBot = leadsByState.GetValueOrDefault("bot", 0);
        var leadsInQueue = leadsByState.GetValueOrDefault("queue", 0);
        var leadsInService = leadsByState.GetValueOrDefault("service", 0);
        var leadsConcluded = leadsByState.GetValueOrDefault("concluido", 0);

        // ─────────────────────────────────────────────────────────
        // 5. CALCULAR MÉTRICAS AGREGADAS
        // ─────────────────────────────────────────────────────────
        
        var allMetrics = await GetLeadsMetricsAsync(unitId, start, end);

        var avgTimeToFirstResponse = CalcularMedia(
            allMetrics.Select(m => m.TimeToFirstResponseMinutes));

        var avgTimeToResolution = CalcularMedia(
            allMetrics.Select(m => m.TimeToResolutionMinutes));

        var avgTimeInBot = CalcularMedia(
            allMetrics.Select(m => m.TimeInBotMinutes));

        var avgTimeInQueue = CalcularMedia(
            allMetrics.Select(m => m.TimeInQueueMinutes));

        var avgTimeInService = CalcularMedia(
            allMetrics.Select(m => m.TimeInServiceMinutes));

        var delayedCount = allMetrics.Count(m => m.IsDelayed);

        // ─────────────────────────────────────────────────────────
        // 6. PERFORMANCE DE ATENDENTES
        // ─────────────────────────────────────────────────────────
        
        var attendantsPerformance = await GetAttendantsPerformanceAsync(
            unitId, start, end);

        // ─────────────────────────────────────────────────────────
        // 7. RETORNAR RESUMO COMPLETO
        // ─────────────────────────────────────────────────────────
        
        return new ClinicSummaryDto
        {
            ClinicId = unitId,
            ClinicName = unitName,
            PeriodStart = start,
            PeriodEnd = end,
            TotalLeads = totalLeads,
            LeadsInBot = leadsInBot,
            LeadsInQueue = leadsInQueue,
            LeadsInService = leadsInService,
            LeadsConcluded = leadsConcluded,
            AverageTimeToFirstResponseMinutes = avgTimeToFirstResponse,
            AverageTimeToResolutionMinutes = avgTimeToResolution,
            AverageTimeInBotMinutes = avgTimeInBot,
            AverageTimeInQueueMinutes = avgTimeInQueue,
            AverageTimeInServiceMinutes = avgTimeInService,
            DelayedLeadsCount = delayedCount,
            AttendantsPerformance = attendantsPerformance,
            LeadsByState = leadsByState,
            LastCalculatedAt = DateTime.UtcNow
        };
    }

    // ═══════════════════════════════════════════════════════════════
    // PERFORMANCE DE ATENDENTES
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Calcular performance de cada atendente no período
    /// </summary>
    private async Task<List<AttendantPerformanceDto>> GetAttendantsPerformanceAsync(
        int unitId,
        DateTime startDate,
        DateTime endDate)
    {
        // ─────────────────────────────────────────────────────────
        // BUSCAR TODOS OS ATENDENTES DA UNIDADE
        // ─────────────────────────────────────────────────────────
        
        var attendants = await _context.Attendants
            .Where(a => a.UnitId == unitId)
            .ToListAsync();

        var performance = new List<AttendantPerformanceDto>();

        // ─────────────────────────────────────────────────────────
        // CALCULAR MÉTRICAS DE CADA ATENDENTE
        // ─────────────────────────────────────────────────────────
        
        foreach (var attendant in attendants)
        {
            // Leads atribuídos a este atendente (via LeadAssignment)
            var assignedLeadIds = await _context.Set<LeadAssignment>()
                .Where(la => la.AttendantId == attendant.Id
                    && la.AssignedAt >= startDate
                    && la.AssignedAt <= endDate)
                .Select(la => la.LeadId)
                .Distinct()
                .ToListAsync();

            var totalHandled = assignedLeadIds.Count;

            // Leads atualmente em atendimento
            var currentActive = await _context.Leads
                .CountAsync(l => l.AttendantId == attendant.Id 
                    && l.ConversationState == "service");

            // Leads concluídos
            var concluded = await _context.Leads
                .CountAsync(l => assignedLeadIds.Contains(l.Id)
                    && l.ConversationState == "concluido");

            // Tempo médio em atendimento (service)
            var serviceConversations = await _context.LeadConversations
                .Where(lc => assignedLeadIds.Contains(lc.LeadId)
                    && lc.ConversationState == "service"
                    && lc.EndedAt.HasValue)
                .ToListAsync();

            var avgServiceTime = serviceConversations.Any()
                ? serviceConversations
                    .Select(lc => (lc.EndedAt!.Value - lc.StartedAt).TotalMinutes)
                    .Average()
                : 0;

            // Tempo médio até resolução
            var concludedLeads = await _context.Leads
                .Where(l => assignedLeadIds.Contains(l.Id)
                    && l.ConversationState == "concluido"
                    && l.ConvertedAt.HasValue)
                .ToListAsync();

            var avgResolutionTime = concludedLeads.Any()
                ? concludedLeads
                    .Select(l => (l.ConvertedAt!.Value - l.CreatedAt).TotalMinutes)
                    .Average()
                : 0;

            // Taxa de conversão
            var conversionRate = totalHandled > 0 
                ? (double)concluded / totalHandled 
                : 0;

            performance.Add(new AttendantPerformanceDto
            {
                AttendantId = attendant.Id,
                AttendantName = attendant.Name,
                TotalLeadsHandled = totalHandled,
                CurrentActiveLeads = currentActive,
                LeadsConcluded = concluded,
                AverageServiceTimeMinutes = avgServiceTime > 0 ? avgServiceTime : null,
                AverageResolutionTimeMinutes = avgResolutionTime > 0 ? avgResolutionTime : null,
                ConversionRate = conversionRate
            });
        }

        return performance
            .OrderByDescending(p => p.TotalLeadsHandled)
            .ToList();
    }

    // ═══════════════════════════════════════════════════════════════
    // ALERTAS E LEADS ATRASADOS
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Obter apenas leads com alertas (demorando muito)
    /// </summary>
    public async Task<List<LeadMetricsDto>> GetDelayedLeadsAsync(int unitId)
    {
        var allMetrics = await GetLeadsMetricsAsync(unitId);
        return allMetrics.Where(m => m.IsDelayed).ToList();
    }

    // ═══════════════════════════════════════════════════════════════
    // MÉTODOS AUXILIARES (CÁLCULOS E VALIDAÇÕES)
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Calcular tempo total que o lead ficou em um estado específico
    /// </summary>
    private static double CalcularTempoNoEstado(
        List<LeadConversation> conversations, 
        string estado)
    {
        return conversations
            .Where(c => c.ConversationState == estado && c.EndedAt.HasValue)
            .Sum(c => (c.EndedAt!.Value - c.StartedAt).TotalMinutes);
    }

    /// <summary>
    /// Calcular tempo até primeiro atendimento humano (bot/queue → service)
    /// </summary>
    private static double? CalcularTempoAteAtendimento(
        Lead lead, 
        List<LeadConversation> conversations)
    {
        var firstService = conversations
            .FirstOrDefault(c => c.ConversationState == "service");

        return firstService != null
            ? (firstService.StartedAt - lead.CreatedAt).TotalMinutes
            : null;
    }

    /// <summary>
    /// Calcular tempo até resolução completa (criação → concluído)
    /// </summary>
    private static double? CalcularTempoAteResolucao(
        Lead lead,
        List<LeadConversation> conversations)
    {
        var firstConcluido = conversations
            .FirstOrDefault(c => c.ConversationState == "concluido");

        return firstConcluido != null
            ? (firstConcluido.StartedAt - lead.CreatedAt).TotalMinutes
            : null;
    }

    /// <summary>
    /// Verificar se lead está demorando muito (alertas)
    /// </summary>
    private (bool IsDelayed, string? Reason) VerificarAlertas(
        Lead lead,
        List<LeadConversation> conversations)
    {
        var now = DateTime.UtcNow;

        // ─────────────────────────────────────────────────────────
        // ALERTA: BOT (> 30 MINUTOS)
        // ─────────────────────────────────────────────────────────
        
        if (lead.ConversationState == "bot")
        {
            var tempoEmBot = (now - lead.CreatedAt).TotalMinutes;
            
            if (tempoEmBot > ALERTA_BOT_MINUTOS)
            {
                return (true, 
                    $"Em BOT há {tempoEmBot:F0} minutos (limite: {ALERTA_BOT_MINUTOS}min)");
            }
        }

        // ─────────────────────────────────────────────────────────
        // ALERTA: QUEUE (> 15 MINUTOS)
        // ─────────────────────────────────────────────────────────
        
        if (lead.ConversationState == "queue")
        {
            var conversaAtual = conversations
                .FirstOrDefault(c => c.ConversationState == "queue" 
                    && !c.EndedAt.HasValue);

            if (conversaAtual != null)
            {
                var tempoEmFila = (now - conversaAtual.StartedAt).TotalMinutes;
                
                if (tempoEmFila > ALERTA_QUEUE_MINUTOS)
                {
                    return (true, 
                        $"Na fila há {tempoEmFila:F0} minutos (limite: {ALERTA_QUEUE_MINUTOS}min)");
                }
            }
        }

        // ─────────────────────────────────────────────────────────
        // ALERTA: SERVICE (> 120 MINUTOS / 2 HORAS)
        // ─────────────────────────────────────────────────────────
        
        if (lead.ConversationState == "service")
        {
            var conversaAtual = conversations
                .FirstOrDefault(c => c.ConversationState == "service" 
                    && !c.EndedAt.HasValue);

            if (conversaAtual != null)
            {
                var tempoEmAtendimento = (now - conversaAtual.StartedAt).TotalMinutes;
                
                if (tempoEmAtendimento > ALERTA_SERVICE_MINUTOS)
                {
                    return (true, 
                        $"Em atendimento há {tempoEmAtendimento:F0} minutos (limite: {ALERTA_SERVICE_MINUTOS}min)");
                }
            }
        }

        return (false, null);
    }

    /// <summary>
    /// Montar timeline de conversas para exibição
    /// </summary>
    private static List<ConversationPeriodDto> MontarTimeline(
        List<LeadConversation> conversations)
    {
        return conversations.Select(c => new ConversationPeriodDto
        {
            ConversationId = c.Id,
            State = c.ConversationState,
            StartedAt = c.StartedAt,
            EndedAt = c.EndedAt,
            DurationMinutes = c.EndedAt.HasValue
                ? (c.EndedAt.Value - c.StartedAt).TotalMinutes
                : null,
            AttendantId = null, // LeadConversation não rastreia atendente
            AttendantName = null,
            IsActive = !c.EndedAt.HasValue
        }).ToList();
    }

    /// <summary>
    /// Calcular média de valores nullable (ignora nulls)
    /// </summary>
    private static double? CalcularMedia(IEnumerable<double?> valores)
    {
        var valoresValidos = valores.Where(v => v.HasValue).Select(v => v!.Value).ToList();
        
        return valoresValidos.Any() 
            ? valoresValidos.Average() 
            : null;
    }
}