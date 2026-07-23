using LeadAnalytics.Api.Service;
using LeadAnalytics.Api.Service.Spine;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LeadAnalytics.Api.Controllers;

/// <summary>
/// Dados operacionais vindos do sistema clínico (API Spine do Doutor Hérnia).
/// Somente leitura: o Spine é a fonte de verdade do que aconteceu na clínica —
/// agenda, comparecimento, falta — enquanto a Kommo continua dona do comercial.
/// </summary>
[ApiController]
[Authorize]
[Route("api/spine")]
public class SpineController(
    SpineAvaliacoesService avaliacoes,
    SpineAgendaService agenda,
    SpineApiClient client,
    TenantUnitGuard tenantGuard,
    ILogger<SpineController> logger) : ControllerBase
{
    private readonly SpineAvaliacoesService _avaliacoes = avaliacoes;
    private readonly SpineAgendaService _agenda = agenda;
    private readonly SpineApiClient _client = client;
    private readonly TenantUnitGuard _tenantGuard = tenantGuard;
    private readonly ILogger<SpineController> _logger = logger;

    /// <summary>
    /// Card de avaliações: agendadas, comparecimento real, faltas e desmarques na janela.
    /// Padrão: últimos 30 dias. Janela máxima: 99 dias.
    /// </summary>
    [HttpGet("avaliacoes")]
    public async Task<IActionResult> Avaliacoes(
        [FromQuery] int unitId,
        [FromQuery] DateOnly? de,
        [FromQuery] DateOnly? ate,
        CancellationToken ct = default)
    {
        var (error, _) = await _tenantGuard.ResolveTenantAsync(unitId, ct);
        if (error is not null) return error;

        var fim = ate ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var inicio = de ?? fim.AddDays(-30);

        if (fim < inicio)
            return BadRequest(new ProblemDetails { Title = "Período inválido: 'ate' anterior a 'de'.", Status = 400 });

        // 99 e não 100: pedimos sempre um dia a mais à API deles, porque o endDate
        // é exclusivo (ver SpineApiClient.SearchSchedulesAsync).
        if (fim.DayNumber - inicio.DayNumber > SpineApiClient.MaxDiasJanela)
            return BadRequest(new ProblemDetails
            {
                Title = $"A API do Doutor Hérnia aceita no máximo {SpineApiClient.MaxDiasJanela} dias por consulta.",
                Status = 400,
            });

        try
        {
            var dto = await _avaliacoes.GetAsync(unitId, inicio, fim, ct);
            if (dto is null)
                return StatusCode(StatusCodes.Status503ServiceUnavailable, new ProblemDetails
                {
                    Title = "Integração com o Doutor Hérnia não configurada para esta unidade.",
                    Detail = $"Cadastre o token em AppConfiguration '{SpineTokenStore.KeyFor(unitId)}'.",
                    Status = 503,
                });

            return Ok(dto);
        }
        catch (SpineApiException ex)
        {
            // Repassa o motivo em vez de 500 genérico: 401 (token revogado) e 403
            // (módulo não liberado) são situações operacionais, não bug de código.
            _logger.LogWarning(ex, "Falha ao consultar avaliações no Spine (unidade {UnitId})", unitId);
            return StatusCode(StatusCodes.Status502BadGateway, new ProblemDetails
            {
                Title = "Doutor Hérnia recusou a consulta.",
                Detail = ex.Motivo,
                Status = 502,
            });
        }
    }

    /// <summary>
    /// Agenda da clínica no período, para a visão de calendário. Devolve todos os
    /// horários com categoria, profissional e situação, já em horário local.
    /// Padrão: a semana corrente. Janela máxima: 99 dias.
    /// </summary>
    [HttpGet("agenda")]
    public async Task<IActionResult> Agenda(
        [FromQuery] int unitId,
        [FromQuery] DateOnly? de,
        [FromQuery] DateOnly? ate,
        CancellationToken ct = default)
    {
        var (error, _) = await _tenantGuard.ResolveTenantAsync(unitId, ct);
        if (error is not null) return error;

        var hoje = DateOnly.FromDateTime(DateTime.UtcNow);
        var inicio = de ?? hoje.AddDays(-(int)hoje.DayOfWeek + 1);
        var fim = ate ?? inicio.AddDays(5);

        if (fim < inicio)
            return BadRequest(new ProblemDetails { Title = "Período inválido: 'ate' anterior a 'de'.", Status = 400 });

        if (fim.DayNumber - inicio.DayNumber > SpineApiClient.MaxDiasJanela)
            return BadRequest(new ProblemDetails
            {
                Title = $"A API do Doutor Hérnia aceita no máximo {SpineApiClient.MaxDiasJanela} dias por consulta.",
                Status = 400,
            });

        try
        {
            var dto = await _agenda.GetAsync(unitId, inicio, fim, ct);
            if (dto is null)
                return StatusCode(StatusCodes.Status503ServiceUnavailable, new ProblemDetails
                {
                    Title = "Integração com o Doutor Hérnia não configurada para esta unidade.",
                    Detail = $"Cadastre o token em AppConfiguration '{SpineTokenStore.KeyFor(unitId)}'.",
                    Status = 503,
                });

            return Ok(dto);
        }
        catch (SpineApiException ex)
        {
            _logger.LogWarning(ex, "Falha ao consultar agenda no Spine (unidade {UnitId})", unitId);
            return StatusCode(StatusCodes.Status502BadGateway, new ProblemDetails
            {
                Title = "Doutor Hérnia recusou a consulta.",
                Detail = ex.Motivo,
                Status = 502,
            });
        }
    }

    /// <summary>Healthcheck da API do Doutor Hérnia — útil pra Central de Integrações.</summary>
    [HttpGet("status")]
    public async Task<IActionResult> Status(CancellationToken ct = default)
    {
        if (_tenantGuard.RequireTenant(out _) is { } error) return error;
        var up = await _client.IsUpAsync(ct);
        return Ok(new { provider = "doutor-hernia-spine", online = up });
    }
}
