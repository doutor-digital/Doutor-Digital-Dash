using LeadAnalytics.Api.Service;
using Microsoft.AspNetCore.Mvc;

namespace LeadAnalytics.Api.Controllers;

[ApiController]
[Route("api/analytics")]
public class LeadAnalyticsController(
    LeadAnalyticsService analyticsService,
    ILogger<LeadAnalyticsController> logger) : ControllerBase
{
    private readonly LeadAnalyticsService _analyticsService = analyticsService;
    private readonly ILogger<LeadAnalyticsController> _logger = logger;

    /// <summary>
    /// Obter métricas completas de um lead específico
    /// </summary>
    /// <param name="id">ID do lead</param>
    /// <returns>Métricas detalhadas incluindo timeline e tempos por estado</returns>
    [HttpGet("leads/{id}/metrics")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetLeadMetrics(int id)
    {
        try
        {
            var metrics = await _analyticsService.GetLeadMetricsAsync(id);

            if (metrics == null)
            {
                return NotFound(new { message = $"Lead {id} não encontrado" });
            }

            _logger.LogInformation("Métricas do lead {LeadId} consultadas", id);
            return Ok(metrics);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao obter métricas do lead {LeadId}", id);
            return StatusCode(500, new { message = "Erro ao obter métricas" });
        }
    }

    /// <summary>
    /// Obter métricas de múltiplos leads com filtros
    /// </summary>
    /// <param name="unitId">ID da unidade</param>
    /// <param name="startDate">Data inicial (opcional)</param>
    /// <param name="endDate">Data final (opcional)</param>
    /// <param name="state">Estado específico (opcional): bot, queue, service, concluido</param>
    /// <returns>Lista de métricas de leads</returns>
    [HttpGet("units/{unitId}/leads-metrics")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetLeadsMetrics(
        int unitId,
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null,
        [FromQuery] string? state = null)
    {
        try
        {
            var metrics = await _analyticsService.GetLeadsMetricsAsync(
                unitId,
                startDate,
                endDate,
                state);

            if(_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation(
                    "Métricas de {Count} leads consultadas para unidade {UnitId}",
                    metrics.Count,
                    unitId);
            }

            return Ok(new
            {
                unitId,
                period = new
                {
                    start = startDate,
                    end = endDate
                },
                state,
                totalLeads = metrics.Count,
                metrics
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao obter métricas de leads da unidade {UnitId}", unitId);
            return StatusCode(500, new { message = "Erro ao obter métricas" });
        }
    }

    /// <summary>
    /// Obter resumo agregado da clínica
    /// </summary>
    /// <param name="unitId">ID da unidade</param>
    /// <param name="startDate">Data inicial (padrão: 30 dias atrás)</param>
    /// <param name="endDate">Data final (padrão: hoje)</param>
    /// <returns>Resumo com médias, totais e performance de atendentes</returns>
    [HttpGet("units/{unitId}/summary")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetClinicSummary(
        int unitId,
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null)
    {
        try
        {
            var summary = await _analyticsService.GetClinicSummaryAsync(
                unitId,
                startDate,
                endDate);

            _logger.LogInformation(
                "Resumo da unidade {UnitId} consultado para período {Start} - {End}",
                unitId,
                summary.PeriodStart,
                summary.PeriodEnd);

            return Ok(summary);
        }
        catch (ArgumentException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao obter resumo da unidade {UnitId}", unitId);
            return StatusCode(500, new { message = "Erro ao obter resumo" });
        }
    }

    /// <summary>
    /// Obter leads com alertas (demorando muito)
    /// </summary>
    /// <param name="unitId">ID da unidade</param>
    /// <returns>Lista de leads que estão demorando acima dos limites</returns>
    [HttpGet("units/{unitId}/alerts")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetDelayedLeads(int unitId)
    {
        try
        {
            var delayedLeads = await _analyticsService.GetDelayedLeadsAsync(unitId);

            _logger.LogInformation(
                "{Count} leads com alertas encontrados na unidade {UnitId}",
                delayedLeads.Count,
                unitId);

            return Ok(new
            {
                unitId,
                totalDelayed = delayedLeads.Count,
                limits = new
                {
                    bot = "30 minutos",
                    queue = "15 minutos",
                    service = "120 minutos"
                },
                leads = delayedLeads
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao obter leads com alertas da unidade {UnitId}", unitId);
            return StatusCode(500, new { message = "Erro ao obter alertas" });
        }
    }

    /// <summary>
    /// Obter dashboard do dia (resumo rápido)
    /// </summary>
    /// <param name="unitId">ID da unidade</param>
    /// <returns>Métricas do dia atual</returns>
    [HttpGet("units/{unitId}/dashboard/today")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTodayDashboard(int unitId)
    {
        try
        {
            var today = DateTime.UtcNow.Date;
            var summary = await _analyticsService.GetClinicSummaryAsync(
                unitId,
                today,
                DateTime.UtcNow);

            var delayedLeads = await _analyticsService.GetDelayedLeadsAsync(unitId);

            return Ok(new
            {
                unitId,
                date = today,
                summary = new
                {
                    summary.TotalLeads,
                    summary.LeadsInBot,
                    summary.LeadsInQueue,
                    summary.LeadsInService,
                    summary.LeadsConcluded,
                    summary.AverageTimeToFirstResponseMinutes,
                    summary.DelayedLeadsCount
                },
                alerts = delayedLeads.Select(l => new
                {
                    l.LeadId,
                    l.Name,
                    l.Phone,
                    l.CurrentState,
                    l.DelayReason
                }),
                topAttendants = summary.AttendantsPerformance
                    .OrderByDescending(a => a.LeadsConcluded)
                    .Take(5)
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao obter dashboard do dia para unidade {UnitId}", unitId);
            return StatusCode(500, new { message = "Erro ao obter dashboard" });
        }
    }
}