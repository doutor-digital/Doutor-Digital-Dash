using LeadAnalytics.Api.Service;
using LeadAnalytics.Api.Service.Spine;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

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
    SpinePacienteService pacientes,
    SpineRedeService rede,
    SpineTokenStore tokens,
    SpineApiClient client,
    TenantUnitGuard tenantGuard,
    ILogger<SpineController> logger) : ControllerBase
{
    private readonly SpineAvaliacoesService _avaliacoes = avaliacoes;
    private readonly SpineAgendaService _agenda = agenda;
    private readonly SpinePacienteService _pacientes = pacientes;
    private readonly SpineRedeService _rede = rede;
    private readonly SpineTokenStore _tokens = tokens;
    private readonly SpineApiClient _client = client;
    private readonly TenantUnitGuard _tenantGuard = tenantGuard;
    private readonly ILogger<SpineController> _logger = logger;

    /// <summary>
    /// Comparativo entre as unidades da rede (avaliações + comparecimento por
    /// unidade), para o franqueador master. Só as unidades com token conectado
    /// entram no ranking; as demais vêm em "semToken". Padrão: últimos 30 dias.
    /// </summary>
    [HttpGet("rede/comparativo")]
    public async Task<IActionResult> RedeComparativo(
        [FromQuery] DateOnly? de,
        [FromQuery] DateOnly? ate,
        CancellationToken ct = default)
    {
        if (_tenantGuard.RequireTenant(out var tenantId) is { } error) return error;

        var fim = ate ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var inicio = de ?? fim.AddDays(-30);
        if (fim < inicio)
            return BadRequest(new ProblemDetails { Title = "Período inválido: 'ate' anterior a 'de'.", Status = 400 });
        if (fim.DayNumber - inicio.DayNumber > SpineApiClient.MaxDiasJanela)
            return BadRequest(new ProblemDetails
            {
                Title = $"A API do Doutor Hérnia aceita no máximo {SpineApiClient.MaxDiasJanela} dias por consulta.",
                Status = 400,
            });

        var dto = await _rede.ComparativoAsync(tenantId, inicio, fim, ct);
        return Ok(dto);
    }

    // ─── Onboarding self-service do token (Central de Integrações) ───────────

    public record SalvarTokenBody(string Token);

    /// <summary>Status da integração da unidade: configurado? quando? prévia mascarada + online.</summary>
    [HttpGet("config")]
    public async Task<IActionResult> ConfigStatus([FromQuery] int unitId, CancellationToken ct = default)
    {
        var (error, _) = await _tenantGuard.ResolveTenantAsync(unitId, ct);
        if (error is not null) return error;

        var (configurado, atualizado, previa) = await _tokens.GetStatusAsync(unitId, ct);
        return Ok(new { unitId, configurado, atualizadoEm = atualizado, previa });
    }

    /// <summary>
    /// Grava o token da unidade e valida na hora contra a API do Doutor Hérnia.
    /// O token é cifrado antes de persistir. Rejeita antes de salvar se for inválido,
    /// pra unidade não achar que conectou quando o token está errado/revogado.
    /// </summary>
    [HttpPut("config")]
    public async Task<IActionResult> SalvarToken(
        [FromQuery] int unitId, [FromBody] SalvarTokenBody body, CancellationToken ct = default)
    {
        var (error, _) = await _tenantGuard.ResolveTenantAsync(unitId, ct);
        if (error is not null) return error;

        var token = body?.Token?.Trim();
        if (string.IsNullOrWhiteSpace(token) || token.Length < 20)
            return BadRequest(new ProblemDetails { Title = "Token inválido ou muito curto.", Status = 400 });

        var (ok, motivo) = await _client.ValidateTokenAsync(token, ct);
        if (!ok)
            return BadRequest(new ProblemDetails
            {
                Title = "O Doutor Hérnia recusou esse token.",
                Detail = motivo,
                Status = 400,
            });

        await _tokens.SaveTokenAsync(unitId, token, ct);
        var (_, atualizado, previa) = await _tokens.GetStatusAsync(unitId, ct);
        return Ok(new { unitId, configurado = true, atualizadoEm = atualizado, previa });
    }

    /// <summary>Remove o token da unidade (desconecta).</summary>
    [HttpDelete("config")]
    public async Task<IActionResult> RemoverToken([FromQuery] int unitId, CancellationToken ct = default)
    {
        var (error, _) = await _tenantGuard.ResolveTenantAsync(unitId, ct);
        if (error is not null) return error;

        var removido = await _tokens.DeleteTokenAsync(unitId, ct);
        return Ok(new { unitId, removido });
    }

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

    /// <summary>
    /// Ficha do paciente a partir do NOME (o que o calendário tem ao clicar num
    /// horário). Resolve nome → cadastro: 1 exato devolve a ficha completa com
    /// histórico; nome duplicado devolve candidatos para escolher; nenhum devolve
    /// vazio. Não é 404 quando não acha — devolve 200 com detalhe null.
    /// </summary>
    [HttpGet("paciente")]
    public async Task<IActionResult> PacientePorNome(
        [FromQuery] int unitId,
        [FromQuery] string nome,
        CancellationToken ct = default)
    {
        var (error, _) = await _tenantGuard.ResolveTenantAsync(unitId, ct);
        if (error is not null) return error;

        if (string.IsNullOrWhiteSpace(nome) || nome.Trim().Length < 2)
            return BadRequest(new ProblemDetails { Title = "Informe um nome com ao menos 2 caracteres.", Status = 400 });

        try
        {
            var dto = await _pacientes.PorNomeAsync(unitId, nome, ct);
            if (dto is null) return SemToken(unitId);
            return Ok(dto);
        }
        catch (SpineApiException ex)
        {
            _logger.LogWarning(ex, "Falha ao resolver paciente '{Nome}' (unidade {UnitId})", nome, unitId);
            return BadGateway(ex);
        }
    }

    /// <summary>Ficha do paciente pelo idClient (quando o usuário escolheu um candidato).</summary>
    [HttpGet("paciente/{idClient:long}")]
    public async Task<IActionResult> PacientePorId(
        [FromQuery] int unitId,
        long idClient,
        CancellationToken ct = default)
    {
        var (error, _) = await _tenantGuard.ResolveTenantAsync(unitId, ct);
        if (error is not null) return error;

        try
        {
            var token = await _pacientes.PorIdAsync(unitId, idClient, ct);
            if (token is null)
                return NotFound(new ProblemDetails { Title = "Paciente não encontrado.", Status = 404 });
            return Ok(token);
        }
        catch (SpineApiException ex)
        {
            _logger.LogWarning(ex, "Falha ao buscar paciente {IdClient} (unidade {UnitId})", idClient, unitId);
            return BadGateway(ex);
        }
    }

    private IActionResult SemToken(int unitId) =>
        StatusCode(StatusCodes.Status503ServiceUnavailable, new ProblemDetails
        {
            Title = "Integração com o Doutor Hérnia não configurada para esta unidade.",
            Detail = $"Cadastre o token em AppConfiguration '{SpineTokenStore.KeyFor(unitId)}'.",
            Status = 503,
        });

    private IActionResult BadGateway(SpineApiException ex) =>
        StatusCode(StatusCodes.Status502BadGateway, new ProblemDetails
        {
            Title = "Doutor Hérnia recusou a consulta.",
            Detail = ex.Motivo,
            Status = 502,
        });

    /// <summary>
    /// Histórico de avaliações do nosso banco (snapshot), agrupado por mês. Lê do
    /// banco, então NÃO tem o limite de 100 dias da API do Doutor Hérnia — é a série
    /// longa que a franquia não consegue ver em lugar nenhum. Só há dado a partir do
    /// dia em que a captura foi ligada.
    /// </summary>
    [HttpGet("historico")]
    public async Task<IActionResult> Historico(
        [FromQuery] int unitId,
        [FromQuery] int meses = 12,
        [FromServices] Data.AppDbContext db = null!,
        CancellationToken ct = default)
    {
        var (error, _) = await _tenantGuard.ResolveTenantAsync(unitId, ct);
        if (error is not null) return error;

        var desde = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(-Math.Clamp(meses, 1, 60));

        // Só avaliações (idCategory 1) — a métrica que fecha o funil comercial.
        var linhas = await db.SpineScheduleSnapshots
            .AsNoTracking()
            .Where(s => s.UnitId == unitId
                        && s.IdCategory == SpineApiClient.ScheduleCategory.Avaliacao
                        && s.DiaLocal >= desde)
            .GroupBy(s => new { s.DiaLocal.Year, s.DiaLocal.Month })
            .Select(g => new
            {
                ano = g.Key.Year,
                mes = g.Key.Month,
                agendadas = g.Count(),
                compareceram = g.Count(x => x.IdStatus == SpineApiClient.ScheduleStatus.Atendido),
                naoCompareceram = g.Count(x => x.IdStatus == SpineApiClient.ScheduleStatus.NaoCompareceu),
                desmarcadas = g.Count(x => x.IdStatus == SpineApiClient.ScheduleStatus.Desmarcado),
            })
            .OrderBy(x => x.ano).ThenBy(x => x.mes)
            .ToListAsync(ct);

        var meta = await db.SpineScheduleSnapshots
            .AsNoTracking()
            .Where(s => s.UnitId == unitId)
            .OrderBy(s => s.DiaLocal)
            .Select(s => (DateOnly?)s.DiaLocal)
            .FirstOrDefaultAsync(ct);

        var serie = linhas.Select(x => new
        {
            competencia = $"{x.ano:D4}-{x.mes:D2}",
            x.agendadas,
            x.compareceram,
            x.naoCompareceram,
            x.desmarcadas,
            taxaComparecimento = x.agendadas == 0 ? 0 : Math.Round((double)x.compareceram / x.agendadas * 100, 1),
        });

        return Ok(new { unitId, capturandoDesde = meta, serie });
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
