using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.Service;
using Microsoft.AspNetCore.Mvc;

namespace LeadAnalytics.Api.Controllers;

[ApiController]
[Route("metrics")]
public class MetricsController(
    MetricsService metricsService,
    ILogger<MetricsController> logger) : ControllerBase
{
    private readonly MetricsService _metricsService = metricsService;
    private readonly ILogger<MetricsController> _logger = logger;

    [HttpGet("dashboard")]
    public async Task<IActionResult> Dashboard(
        [FromQuery] int clinicId,
        [FromQuery] string attendantType = "HUMAN")
    {
        var result = await _metricsService.GetDashboardAsync(clinicId, attendantType);

        if (result is null)
            return StatusCode(502, "Erro ao buscar métricas da Cloudia.");

        return Ok(result);
    }

    [HttpGet("resumo")]
    public async Task<IActionResult> Resumo([FromQuery] int clinicId)
    {
        var result = await _metricsService.GetDashboardAsync(clinicId, "HUMAN");

        if (result is null)
            return StatusCode(502, "Erro ao buscar métricas da Cloudia.");

        return Ok(new
        {
            TotalEmAtendimento = result.Metrics.TotalInService,
            TotalNaFila = result.Metrics.TotalInQueue,
            TempoMedioResposta = Math.Round(result.Metrics.WaitResponseTimeAvg, 1),
            AtendentesAtivos = result.AttendantsServicesList
                .Where(a => a.AttendantId is not null)
                .Select(a => new
                {
                    Nome = a.AttendantName,
                    EmAtendimento = a.TotalServices,
                    AguardandoRes = a.TotalWaitingForResponse
                })
        });
    }

    [HttpGet("fila")]
    public async Task<IActionResult> Fila([FromQuery] int clinicId)
    {
        var result = await _metricsService.GetDashboardAsync(clinicId, "HUMAN");

        if (result is null)
            return StatusCode(502, "Erro ao buscar métricas da Cloudia.");

        return Ok(new
        {
            NaFila = result.WaitingInQueueList,
            AguardandoResposta = result.WaitingForResponseList
                .OrderByDescending(x => x.WaitingInMinutes)
        });
    }

    [HttpGet("completo")]
    public async Task<IActionResult> Completo(
        [FromQuery] int clinicId,
        [FromServices] AppDbContext db)
    {
        var result = await _metricsService.GetDashboardComHistoricoAsync(clinicId, db);

        if (result is null)
            return StatusCode(502, "Erro ao buscar métricas.");

        return Ok(result);
    }
}