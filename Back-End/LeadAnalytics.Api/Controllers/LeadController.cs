using LeadAnalytics.Api.DTOs.Cloudia;
using LeadAnalytics.Api.DTOs.Response;
using LeadAnalytics.Api.Service;
using Microsoft.AspNetCore.Mvc;

namespace LeadAnalytics.Api.Controllers;

[ApiController]
[Route("webhooks")]
public class WebhooksController(
    LeadService leadService,
    ILogger<WebhooksController> logger) : ControllerBase
{
    private readonly LeadService _leadService = leadService;
    private readonly ILogger<WebhooksController> _logger = logger;

    [HttpGet]
    public async Task<IActionResult> GetAllLeads()
    {
        var leads = await _leadService.GetAllLeadsAsync();
        return Ok(leads);
    }

    /// <summary>
    /// Obter detalhes completos de um lead específico
    /// </summary>
    /// <param name="id">Id interno do lead</param>
    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(LeadDetailDto), 200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetLeadById(int id)
    {
        var lead = await _leadService.GetLeadByIdAsync(id);

        if (lead is null)
        {
            return NotFound(new ProblemDetails
            {
                Title = "Lead não encontrado",
                Status = 404,
                Detail = $"Nenhum lead encontrado com id {id}"
            });
        }

        return Ok(lead);
    }

    [HttpPost("cloudia")]
    public async Task<IActionResult> Cloudia([FromBody] CloudiaWebhookDto dto)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var result = await _leadService.SaveLeadAsync(dto);
        return Ok(result);
    }
    [HttpGet("/webhooks/total-leads")]
    public async Task<IActionResult> GetTotalLeads(int clinicId)
    {
        var total = await _leadService.GetLeadsTotal(clinicId);

        return Ok(new TotalLeadsDto
        {
            Total = total
        });
    }
    /// <summary>
    /// Contar quantos leads estão em atendimento
    /// </summary>
    /// <param name="unitId">Filtrar por unidade (opcional)</param>
    /// <returns>Número de leads em atendimento</returns>
    [HttpGet("in-service/count")]
    public async Task<IActionResult> GetInServiceCount([FromQuery] int? unitId = null)
    {
        var count = await _leadService.GetLeadsInServiceCountAsync(unitId);
        return Ok(new { inService = count });
    }

    /// <summary>
    /// Contar leads em cada estado (detalhado)
    /// </summary>
    /// <param name="unitId">Filtrar por unidade (opcional)</param>
    /// <returns>Contagem por estado</returns>
    [HttpGet("in-service/details")]
    public async Task<IActionResult> GetInServiceDetails([FromQuery] int? unitId = null)
    {
        var details = await _leadService.GetLeadsInServiceDetailsAsync(unitId);
        return Ok(details);
    }

    [HttpGet("consultas")]
    public async Task<IActionResult> GetHasAppoiment(int clinicId)
    {
        var result = await _leadService.GetCheckClosedQueries(clinicId);
        return Ok(result);
    }


    [HttpGet("sem-pagamento")]
    public async Task<IActionResult> GetLeadsWithoutPayment(int clinicId)
    {

        var result = await _leadService.GetCheckStageWithoutPayment(clinicId);

        if (result == 0)
        {
            return NotFound(new ProblemDetails
            {
                Title = "Nenhuma consulta agendada sem pagamento",
                Status = 404
            });
        }

        return Ok(new
        {
            mensagem = "Agendados sem pagamento",
            result
        });
    }

    [HttpGet("com-pagamento")]
    public async Task<IActionResult> VerificarEtapaComPagamento(int clinicId)
    {
        var quantidade = await _leadService.GetVerifyPaymentStep(clinicId);

        if (quantidade == 0)
        {
            return NotFound(new ProblemDetails
            {
                Title = "Nenhuma consulta agendada com pagamento",
                Status = 404
            });
        }

        return Ok(new
        {
            mensagem = "Agendados com pagamento",
            quantidade
        });
    }

    [HttpGet("source-final")]
    public async Task<IActionResult> GetSourceFinally(int clinicId)
    {
        var result = await _leadService.GetVerifySourceFinal(clinicId);
        return Ok(result);
    }

    [HttpGet("origem-cloudia")]
    public async Task<IActionResult> GetOrigens(int clinicId)
    {
        var result = await _leadService.GetCheckSourceCloudia(clinicId);
        return Ok(result);
    }

    [HttpGet("fim-de-semana")]
    public async Task<IActionResult> GetLeadsFinaldeSemana(int clinicId)
    {
        var leads = await _leadService.GetWeekendLeads(clinicId);
        return Ok(leads);
    }

    [HttpGet("etapa-agrupada")]
    public async Task<IActionResult> GetEtapaAgrupada([FromQuery] int clinicId)
    {
        var result = await _leadService.GetCheckGroupedStep(clinicId);

        if (clinicId <= 0)
            return BadRequest("clinicId inválido");

        return Ok(result);
    }

    [HttpGet("buscar-inicio-fim")]
    public async Task<IActionResult> GetBuscarInicioFim([FromQuery] int clinicId, [FromQuery] DateTime dataInicio, [FromQuery] DateTime dataFim)
    {
        if (clinicId <= 0)
            return BadRequest("clinicId inválido");
        if (dataInicio > dataFim)
            return BadRequest("dataInicio deve ser menor ou igual a dataFim");
        var result = await _leadService.GetSearchStartMonthLeads(clinicId, dataInicio, dataFim);
        return Ok(result);
    }

    [HttpGet("consulta-periodos")]
    public async Task<IActionResult> GetConsultaPeriodos([FromQuery] FiltroLeadsPeriodoDto filtro)
    {
        if (filtro.ClinicId <= 0)
            return BadRequest("clinicId inválido");
        if (filtro.Ano <= 0)
            return BadRequest("Ano inválido");
        if (filtro.Mes.HasValue && (filtro.Mes < 1 || filtro.Mes > 12))
            return BadRequest("Mês deve ser entre 1 e 12");
        if (filtro.Dia.HasValue && (filtro.Dia < 1 || filtro.Dia > 31))
            return BadRequest("Dia deve ser entre 1 e 31");

        var result = await _leadService.GetQueryLeadsByPeriodService(filtro);
        return Ok(result);
    }


    /// <summary>
    /// Obter leads ativos para sincronização com n8n
    /// </summary>
    /// <param name="limit">Limite de leads (padrão: 100, máx: 500)</param>
    /// <param name="unitId">Filtrar por unidade específica</param>
    /// <returns>Lista de leads ativos</returns>
    [HttpGet("active")]
    [ProducesResponseType(typeof(List<ActiveLeadDto>), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(500)]
    public async Task<IActionResult> GetActiveLeads(
        [FromQuery] int limit = 100, 
        [FromQuery] int? unitId = null)
    {
        try
        {
            // Validar limite
            if (limit < 1 || limit > 500)
            {
                return BadRequest(new { 
                    error = "Limite deve estar entre 1 e 500",
                    limit = limit 
                });
            }
 
            _logger.LogInformation(
                "📊 GET /api/leads/active - limit={Limit}, unitId={UnitId}", 
                limit, unitId);
 
            var activeLeads = await _leadService.GetActiveLeadsAsync(limit, unitId);
 
            return Ok(activeLeads);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Erro ao buscar leads ativos");
            return StatusCode(500, new { 
                error = "Erro ao buscar leads ativos",
                message = ex.Message 
            });
        }
    }
 
    /// <summary>
    /// Leads criados nas últimas N horas (notificação + página de recentes).
    /// </summary>
    [HttpGet("recent")]
    [ProducesResponseType(typeof(RecentLeadsResponseDto), 200)]
    [ProducesResponseType(400)]
    public async Task<IActionResult> GetRecentLeads(
        [FromQuery] int clinicId,
        [FromQuery] int hours = 24,
        [FromQuery] int limit = 50,
        [FromQuery] int? unitId = null)
    {
        if (clinicId <= 0) return BadRequest(new { error = "clinicId inválido" });
        if (hours <= 0) return BadRequest(new { error = "hours deve ser > 0" });

        var result = await _leadService.GetRecentLeadsAsync(clinicId, hours, limit, unitId, HttpContext.RequestAborted);
        return Ok(result);
    }

    /// <summary>
    /// Série temporal de leads para o dashboard: intervalo + granularidade + comparação.
    /// group_by: day | week | month | quarter. compare: none | previous_period | previous_year.
    /// </summary>
    [HttpGet("evolution-range")]
    [ProducesResponseType(typeof(DashboardEvolutionDto), 200)]
    [ProducesResponseType(400)]
    public async Task<IActionResult> GetEvolutionRange(
        [FromQuery] int clinicId,
        [FromQuery] DateTime dateFrom,
        [FromQuery] DateTime dateTo,
        [FromQuery] string groupBy = "day",
        [FromQuery] string compare = "none")
    {
        if (clinicId <= 0) return BadRequest(new { error = "clinicId inválido" });
        if (dateTo < dateFrom) return BadRequest(new { error = "dateTo deve ser >= dateFrom" });
        if ((dateTo - dateFrom).TotalDays > 3 * 365)
            return BadRequest(new { error = "intervalo máximo permitido é 3 anos" });

        if (!TryParseGranularity(groupBy, out var g))
            return BadRequest(new { error = $"groupBy inválido (use day|week|month|quarter), recebido '{groupBy}'" });
        if (!TryParseCompare(compare, out var c))
            return BadRequest(new { error = $"compare inválido (use none|previous_period|previous_year), recebido '{compare}'" });

        try
        {
            var result = await _leadService.GetEvolutionRangeAsync(
                clinicId, dateFrom, dateTo, g, c, HttpContext.RequestAborted);
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    private static bool TryParseGranularity(string raw, out LeadService.Granularity g)
    {
        switch (raw?.ToLowerInvariant())
        {
            case "day": g = LeadService.Granularity.Day; return true;
            case "week": g = LeadService.Granularity.Week; return true;
            case "month": g = LeadService.Granularity.Month; return true;
            case "quarter": g = LeadService.Granularity.Quarter; return true;
            default: g = LeadService.Granularity.Day; return false;
        }
    }

    private static bool TryParseCompare(string raw, out LeadService.CompareMode c)
    {
        switch (raw?.ToLowerInvariant())
        {
            case "none": c = LeadService.CompareMode.None; return true;
            case "previous_period": c = LeadService.CompareMode.PreviousPeriod; return true;
            case "previous_year": c = LeadService.CompareMode.PreviousYear; return true;
            default: c = LeadService.CompareMode.None; return false;
        }
    }

    /// <summary>
    /// Obter contagem de leads por estado
    /// </summary>
    /// <param name="unitId">Filtrar por unidade específica</param>
    /// <returns>Contagem por estado</returns>
    [HttpGet("count-by-state")]
    [ProducesResponseType(typeof(LeadsCountDto), 200)]
    [ProducesResponseType(500)]
    public async Task<IActionResult> GetLeadsCountByState([FromQuery] int? unitId = null)
    {
        try
        {
            _logger.LogInformation(
                "📊 GET /api/leads/count-by-state - unitId={UnitId}", 
                unitId);
 
            var counts = await _leadService.GetLeadsCountByStateAsync(unitId);
 
            var response = new LeadsCountDto
            {
                Bot = counts.GetValueOrDefault("bot", 0),
                Queue = counts.GetValueOrDefault("queue", 0),
                Service = counts.GetValueOrDefault("service", 0),
                Concluido = counts.GetValueOrDefault("concluido", 0),
                Total = counts.Values.Sum()
            };
 
            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Erro ao contar leads por estado");
            return StatusCode(500, new { 
                error = "Erro ao contar leads",
                message = ex.Message 
            });
        }
    }
 
    /// <summary>
    /// Dados consolidados para a página de Evolução avançada.
    /// Retorna séries mensais com acumulado/MoM/média-móvel, dia-da-semana, hora do dia,
    /// origens ao longo do tempo e conversão mês a mês.
    /// </summary>
    [HttpGet("evolution/advanced")]
    [ProducesResponseType(typeof(EvolutionAdvancedDto), 200)]
    public async Task<IActionResult> GetEvolutionAdvanced(
        [FromQuery] DateTime dataInicio,
        [FromQuery] DateTime dataFim,
        [FromQuery] int? clinicId = null)
    {
        if (dataInicio > dataFim)
            return BadRequest("dataInicio deve ser menor ou igual a dataFim");

        var result = await _leadService.GetEvolutionAdvancedAsync(clinicId, dataInicio, dataFim);
        return Ok(result);
    }

    /// <summary>
    /// Leads capturados durante a madrugada (20h → 07h) da unidade de Araguaína por padrão.
    /// </summary>
    /// <param name="clinicId">Tenant/clínica. Padrão: 8020 (Araguaína).</param>
    /// <param name="startHour">Hora de início (default: 20)</param>
    /// <param name="endHour">Hora final do dia seguinte (default: 7)</param>
    [HttpGet("amanheceu")]
    [ProducesResponseType(typeof(OvernightLeadsDto), 200)]
    public async Task<IActionResult> GetOvernightLeads(
        [FromQuery] int? clinicId = null,
        [FromQuery] int startHour = 20,
        [FromQuery] int endHour = 7)
    {
        if (startHour < 0 || startHour > 23 || endHour < 0 || endHour > 23)
            return BadRequest("startHour e endHour devem estar entre 0 e 23");

        var result = await _leadService.GetOvernightLeadsAsync(clinicId, null, startHour, endHour);
        return Ok(result);
    }

    /// <summary>
    /// Verificar saúde do endpoint de sincronização
    /// </summary>
    [HttpGet("sync/health")]
    [ProducesResponseType(200)]
    public IActionResult GetSyncHealth()
    {
        return Ok(new
        {
            status = "healthy",
            timestamp = DateTime.UtcNow,
            endpoints = new[]
            {
                "/api/leads/active - Leads ativos para sync",
                "/api/leads/count-by-state - Contagem por estado"
            }
        });
    }
}