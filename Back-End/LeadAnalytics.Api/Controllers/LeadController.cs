using LeadAnalytics.Api.DTOs.Cloudia;
using LeadAnalytics.Api.DTOs.Response;
using LeadAnalytics.Api.DTOs.Timeline;
using LeadAnalytics.Api.Service;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LeadAnalytics.Api.Controllers;

[ApiController]
[Authorize]
[Route("webhooks")]
public class WebhooksController(
    LeadService leadService,
    LeadTimelineService timelineService,
    ICurrentUser currentUser,
    TenantUnitGuard tenantGuard,
    ILogger<WebhooksController> logger) : ControllerBase
{
    private readonly LeadService _leadService = leadService;
    private readonly LeadTimelineService _timelineService = timelineService;
    private readonly ICurrentUser _currentUser = currentUser;
    private readonly TenantUnitGuard _tenantGuard = tenantGuard;
    private readonly ILogger<WebhooksController> _logger = logger;

    [HttpGet]
    public async Task<IActionResult> GetAllLeads([FromQuery] int? unitId = null, CancellationToken ct = default)
    {
        var (error, tenantId) = await _tenantGuard.ResolveTenantAsync(unitId, ct);
        if (error is not null) return error;

        var leads = await _leadService.GetAllLeadsAsync(tenantId, unitId);
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
        if (_tenantGuard.RequireTenant(out var tenantId) is { } denied) return denied;

        var lead = await _leadService.GetLeadByIdAsync(id, tenantId);

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

    /// <summary>
    /// Timeline detalhada do lead: stages com tempo em cada um, atribuições,
    /// conversas, interações, atribuição de origem (CTWA) e insights agregados.
    /// </summary>
    [HttpGet("{id:int}/timeline")]
    [ProducesResponseType(typeof(LeadTimelineDto), 200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetLeadTimeline(int id, CancellationToken ct)
    {
        if (_tenantGuard.RequireTenant(out var tenantId) is { } denied) return denied;

        var preflight = await _leadService.GetLeadByIdAsync(id, tenantId);
        if (preflight is null)
        {
            return NotFound(new ProblemDetails
            {
                Title = "Lead não encontrado",
                Status = 404,
                Detail = $"Nenhum lead encontrado com id {id}"
            });
        }

        var timeline = await _timelineService.GetTimelineAsync(id, ct);
        if (timeline is null)
        {
            return NotFound(new ProblemDetails
            {
                Title = "Lead não encontrado",
                Status = 404,
                Detail = $"Nenhum lead encontrado com id {id}"
            });
        }
        return Ok(timeline);
    }

    [HttpPost("cloudia")]
    [AllowAnonymous]
    public async Task<IActionResult> Cloudia([FromBody] CloudiaWebhookDto dto)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var result = await _leadService.SaveLeadAsync(dto);
        return Ok(result);
    }

    /// <summary>
    /// Marcar comparecimento (ou falta) de um lead com consulta agendada.
    /// Body: { attended: bool, outcome?: "fechou"|"nao_fechou", notes?: string }
    /// Quando attended=false, lead vai pra 07_FALTOU. Quando attended=true,
    /// outcome="fechou" → 09_FECHOU_TRATAMENTO, "nao_fechou" → 08_NAO_FECHOU_TRATAMENTO.
    /// </summary>
    [HttpPost("{id:int}/attendance")]
    [ProducesResponseType(typeof(LeadProcessResponseDto), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> MarkAttendance(int id, [FromBody] MarkAttendanceDto dto, CancellationToken ct)
    {
        if (_tenantGuard.RequireTenant(out var tenantId) is { } denied) return denied;
        if (tenantId is null)
            return BadRequest(new ProblemDetails { Title = "Operação requer um tenant específico", Status = 400 });

        try
        {
            var result = await _leadService.MarkAttendanceAsync(id, tenantId.Value, dto, ct);
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new ProblemDetails { Title = ex.Message, Status = 400 });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ProblemDetails { Title = ex.Message, Status = 400 });
        }
    }

    /// <summary>
    /// Fila de recuperação comercial: leads que compareceram e não fecharam (08_NAO_FECHOU_TRATAMENTO).
    /// </summary>
    [HttpGet("recuperacao")]
    [ProducesResponseType(typeof(List<RecoveryLeadDto>), 200)]
    public async Task<IActionResult> GetRecoveryQueue(
        [FromQuery] int clinicId, [FromQuery] int? unitId = null, CancellationToken ct = default)
    {
        if (_tenantGuard.EnsureTenantMatches(clinicId) is { } denied) return denied;
        if (unitId.HasValue && await _tenantGuard.EnsureUnitBelongsToTenantAsync(unitId.Value, ct) is { } guard)
            return guard;

        var result = await _leadService.GetRecoveryQueueAsync(clinicId, unitId, ct);
        return Ok(result);
    }

    /// <summary>
    /// Analytics de conversão: totais, taxas, motivos de não-conversão extraídos
    /// das observações e funil por etapa atual.
    /// </summary>
    [HttpGet("conversion-analytics")]
    [ProducesResponseType(typeof(ConversionAnalyticsDto), 200)]
    public async Task<IActionResult> GetConversionAnalytics(
        [FromQuery] int clinicId,
        [FromQuery] DateTime? dateFrom = null,
        [FromQuery] DateTime? dateTo = null,
        [FromQuery] int? unitId = null,
        CancellationToken ct = default)
    {
        if (_tenantGuard.EnsureTenantMatches(clinicId) is { } denied) return denied;
        if (unitId.HasValue && await _tenantGuard.EnsureUnitBelongsToTenantAsync(unitId.Value, ct) is { } guard)
            return guard;

        var result = await _leadService.GetConversionAnalyticsAsync(clinicId, dateFrom, dateTo, unitId, ct);
        return Ok(result);
    }

    /// <summary>
    /// Mudanças de etapa recentes em todos os leads da clínica, com séries para
    /// gráficos (por dia, por destino) e a lista mais recente.
    /// </summary>
    [HttpGet("stage-changes")]
    [ProducesResponseType(typeof(StageChangesSummaryDto), 200)]
    public async Task<IActionResult> GetStageChanges(
        [FromQuery] int clinicId,
        [FromQuery] DateTime? dateFrom = null,
        [FromQuery] DateTime? dateTo = null,
        [FromQuery] int? unitId = null,
        [FromQuery] int limit = 100,
        CancellationToken ct = default)
    {
        if (_tenantGuard.EnsureTenantMatches(clinicId) is { } denied) return denied;
        if (unitId.HasValue && await _tenantGuard.EnsureUnitBelongsToTenantAsync(unitId.Value, ct) is { } guard)
            return guard;

        var result = await _leadService.GetStageChangesAsync(clinicId, dateFrom, dateTo, unitId, limit, ct);
        return Ok(result);
    }
    [HttpGet("/webhooks/total-leads")]
    public async Task<IActionResult> GetTotalLeads(int clinicId)
    {
        if (_tenantGuard.EnsureTenantMatches(clinicId) is { } denied) return denied;

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
    public async Task<IActionResult> GetInServiceCount([FromQuery] int? unitId = null, CancellationToken ct = default)
    {
        var (error, tenantId) = await _tenantGuard.ResolveTenantAsync(unitId, ct);
        if (error is not null) return error;

        var count = await _leadService.GetLeadsInServiceCountAsync(tenantId, unitId);
        return Ok(new { inService = count });
    }

    /// <summary>
    /// Contar leads em cada estado (detalhado)
    /// </summary>
    /// <param name="unitId">Filtrar por unidade (opcional)</param>
    /// <returns>Contagem por estado</returns>
    [HttpGet("in-service/details")]
    public async Task<IActionResult> GetInServiceDetails([FromQuery] int? unitId = null, CancellationToken ct = default)
    {
        var (error, tenantId) = await _tenantGuard.ResolveTenantAsync(unitId, ct);
        if (error is not null) return error;

        var details = await _leadService.GetLeadsInServiceDetailsAsync(tenantId, unitId);
        return Ok(details);
    }

    [HttpGet("consultas")]
    public async Task<IActionResult> GetHasAppoiment([FromQuery] int clinicId, [FromQuery] int? unitId = null, CancellationToken ct = default)
    {
        if (_tenantGuard.EnsureTenantMatches(clinicId) is { } denied) return denied;
        if (unitId.HasValue && await _tenantGuard.EnsureUnitBelongsToTenantAsync(unitId.Value, ct) is { } guard)
            return guard;

        var result = await _leadService.GetCheckClosedQueries(clinicId, unitId);
        return Ok(result);
    }


    [HttpGet("sem-pagamento")]
    public async Task<IActionResult> GetLeadsWithoutPayment([FromQuery] int clinicId, [FromQuery] int? unitId = null, CancellationToken ct = default)
    {
        if (_tenantGuard.EnsureTenantMatches(clinicId) is { } denied) return denied;
        if (unitId.HasValue && await _tenantGuard.EnsureUnitBelongsToTenantAsync(unitId.Value, ct) is { } guard)
            return guard;

        var result = await _leadService.GetCheckStageWithoutPayment(clinicId, unitId);

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
        if (_tenantGuard.EnsureTenantMatches(clinicId) is { } denied) return denied;

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
        if (_tenantGuard.EnsureTenantMatches(clinicId) is { } denied) return denied;

        var result = await _leadService.GetVerifySourceFinal(clinicId);
        return Ok(result);
    }

    [HttpGet("origem-cloudia")]
    public async Task<IActionResult> GetOrigens(int clinicId)
    {
        if (_tenantGuard.EnsureTenantMatches(clinicId) is { } denied) return denied;

        var result = await _leadService.GetCheckSourceCloudia(clinicId);
        return Ok(result);
    }

    [HttpGet("fim-de-semana")]
    public async Task<IActionResult> GetLeadsFinaldeSemana(int clinicId)
    {
        if (_tenantGuard.EnsureTenantMatches(clinicId) is { } denied) return denied;

        var leads = await _leadService.GetWeekendLeads(clinicId);
        return Ok(leads);
    }

    [HttpGet("etapa-agrupada")]
    public async Task<IActionResult> GetEtapaAgrupada([FromQuery] int clinicId)
    {
        if (_tenantGuard.EnsureTenantMatches(clinicId) is { } denied) return denied;

        var result = await _leadService.GetCheckGroupedStep(clinicId);
        return Ok(result);
    }

    [HttpGet("buscar-inicio-fim")]
    public async Task<IActionResult> GetBuscarInicioFim([FromQuery] int clinicId, [FromQuery] DateTime dataInicio, [FromQuery] DateTime dataFim)
    {
        if (_tenantGuard.EnsureTenantMatches(clinicId) is { } denied) return denied;
        if (dataInicio > dataFim)
            return BadRequest("dataInicio deve ser menor ou igual a dataFim");
        var result = await _leadService.GetSearchStartMonthLeads(clinicId, dataInicio, dataFim);
        return Ok(result);
    }

    [HttpGet("consulta-periodos")]
    public async Task<IActionResult> GetConsultaPeriodos([FromQuery] FiltroLeadsPeriodoDto filtro)
    {
        if (_tenantGuard.EnsureTenantMatches(filtro.ClinicId) is { } denied) return denied;
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
        [FromQuery] int? unitId = null,
        CancellationToken ct = default)
    {
        try
        {
            if (limit < 1 || limit > 500)
            {
                return BadRequest(new {
                    error = "Limite deve estar entre 1 e 500",
                    limit = limit
                });
            }

            var (error, tenantId) = await _tenantGuard.ResolveTenantAsync(unitId, ct);
            if (error is not null) return error;

            _logger.LogInformation(
                "📊 GET /webhooks/active - tenantId={Tenant}, limit={Limit}, unitId={UnitId}",
                tenantId, limit, unitId);

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
    /// Overview consolidado do dashboard. Todos os KPIs filtrados por dateFrom/dateTo
    /// (baseados em Lead.CreatedAt). Usado pela DashboardPage.
    /// </summary>
    [HttpGet("dashboard-overview")]
    [ProducesResponseType(typeof(DashboardOverviewDto), 200)]
    [ProducesResponseType(400)]
    public async Task<IActionResult> GetDashboardOverview(
        [FromQuery] int clinicId,
        [FromQuery] DateTime dateFrom,
        [FromQuery] DateTime dateTo,
        [FromQuery] int? unitId = null)
    {
        if (_tenantGuard.EnsureTenantMatches(clinicId) is { } denied) return denied;
        if (unitId.HasValue && await _tenantGuard.EnsureUnitBelongsToTenantAsync(unitId.Value, HttpContext.RequestAborted) is { } guard)
            return guard;

        if (dateTo < dateFrom) return BadRequest(new { error = "dateTo deve ser >= dateFrom" });
        if ((dateTo - dateFrom).TotalDays > 3 * 365)
            return BadRequest(new { error = "intervalo máximo permitido é 3 anos" });

        try
        {
            var result = await _leadService.GetDashboardOverviewAsync(
                clinicId, dateFrom, dateTo, unitId, HttpContext.RequestAborted);
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
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
        if (_tenantGuard.EnsureTenantMatches(clinicId) is { } denied) return denied;
        if (unitId.HasValue && await _tenantGuard.EnsureUnitBelongsToTenantAsync(unitId.Value, HttpContext.RequestAborted) is { } guard)
            return guard;
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
        if (_tenantGuard.EnsureTenantMatches(clinicId) is { } denied) return denied;
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
    public async Task<IActionResult> GetLeadsCountByState([FromQuery] int? unitId = null, CancellationToken ct = default)
    {
        try
        {
            var (error, tenantId) = await _tenantGuard.ResolveTenantAsync(unitId, ct);
            if (error is not null) return error;

            _logger.LogInformation(
                "📊 GET /webhooks/count-by-state - tenantId={Tenant}, unitId={UnitId}",
                tenantId, unitId);

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
        if (clinicId.HasValue)
        {
            if (_tenantGuard.EnsureTenantMatches(clinicId.Value) is { } denied) return denied;
        }
        else
        {
            if (_tenantGuard.RequireTenant(out var tenantId) is { } denied) return denied;
            clinicId = tenantId;
        }

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
        if (clinicId.HasValue)
        {
            if (_tenantGuard.EnsureTenantMatches(clinicId.Value) is { } denied) return denied;
        }
        else
        {
            if (_tenantGuard.RequireTenant(out var tenantId) is { } denied) return denied;
            clinicId = tenantId;
        }

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