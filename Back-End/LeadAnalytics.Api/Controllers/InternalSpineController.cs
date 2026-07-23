using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.Service;
using LeadAnalytics.Api.Service.Spine;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LeadAnalytics.Api.Controllers;

/// <summary>
/// Resumo operacional por unidade para o n8n disparar alertas no WhatsApp
/// (Evolution API). Só leitura, protegido por X-Admin-Key.
///
/// Divisão de responsabilidade: a API entrega o DADO pronto (avaliações de hoje,
/// agenda de amanhã); o n8n cuida do QUANDO (cron), do TEXTO e do ENVIO via
/// Evolution. A ociosidade fina (horários vagos) fica no n8n, que é onde está a
/// grade de funcionamento da unidade.
/// </summary>
[ApiController]
[Route("internal/spine")]
public class InternalSpineController(
    AppDbContext db,
    SpineAvaliacoesService avaliacoes,
    SpineHistoricoService historico,
    InternalApiKeyGuard guard,
    ILogger<InternalSpineController> logger) : ControllerBase
{
    private readonly AppDbContext _db = db;
    private readonly SpineAvaliacoesService _avaliacoes = avaliacoes;
    private readonly SpineHistoricoService _historico = historico;
    private readonly InternalApiKeyGuard _guard = guard;
    private readonly ILogger<InternalSpineController> _logger = logger;

    /// <summary>
    /// Captura a agenda recente da unidade e grava no nosso banco (preserva o que a
    /// API do Spine perde depois de 100 dias). O n8n só dispara; a API puxa e grava.
    /// Janela padrão: 7 dias (rolling), para corrigir status que mudaram.
    /// </summary>
    [HttpPost("historico/sync")]
    public async Task<IActionResult> SincronizarHistorico(
        [FromHeader(Name = "X-Admin-Key")] string? adminKey,
        [FromQuery] int unitId,
        [FromQuery] int dias = 7,
        CancellationToken ct = default)
    {
        if (!await _guard.IsAuthorizedAsync(adminKey))
            return Unauthorized(new { message = "Acesso negado" });

        try
        {
            var (conectado, gravados) = await _historico.SyncAsync(unitId, dias, ct);
            return Ok(new { unitId, conectado, gravados });
        }
        catch (SpineApiException ex)
        {
            _logger.LogWarning(ex, "Histórico: sync falhou (unidade {UnitId})", unitId);
            return StatusCode(StatusCodes.Status502BadGateway, new { message = ex.Motivo });
        }
    }

    /// <summary>
    /// Resumo do dia de uma unidade: avaliações de hoje (desfecho) e o que está
    /// agendado para amanhã. O n8n formata e envia.
    /// </summary>
    [HttpGet("resumo")]
    public async Task<IActionResult> Resumo(
        [FromHeader(Name = "X-Admin-Key")] string? adminKey,
        [FromQuery] int unitId,
        CancellationToken ct = default)
    {
        if (!await _guard.IsAuthorizedAsync(adminKey))
            return Unauthorized(new { message = "Acesso negado" });

        var unidade = await _db.Units.AsNoTracking()
            .Where(u => u.Id == unitId).Select(u => u.Name).FirstOrDefaultAsync(ct);
        if (unidade is null)
            return NotFound(new { message = "unidade não encontrada" });

        // Dia local (Imperatriz UTC−3): usa a data BRT, não a UTC.
        var hoje = SpineApiClient.DiaLocal(DateTime.UtcNow);
        var amanha = hoje.AddDays(1);

        try
        {
            var dHoje = await _avaliacoes.GetAsync(unitId, hoje, hoje, ct);
            if (dHoje is null)
                return Ok(new { unitId, unidade, conectado = false });

            var dAmanha = await _avaliacoes.GetAsync(unitId, amanha, amanha, ct);

            int SitHoje(int s) => dHoje.PorSituacao.FirstOrDefault(x => x.IdStatus == s)?.Total ?? 0;
            int SitAmanha(int s) => dAmanha?.PorSituacao.FirstOrDefault(x => x.IdStatus == s)?.Total ?? 0;

            return Ok(new
            {
                unitId,
                unidade,
                conectado = true,
                data = hoje.ToString("yyyy-MM-dd"),
                hoje = new
                {
                    avaliacoesAgendadas = dHoje.Total,
                    compareceram = dHoje.Realizadas,
                    faltaram = SitHoje(SpineApiClient.ScheduleStatus.NaoCompareceu),
                    desmarcadas = SitHoje(SpineApiClient.ScheduleStatus.Desmarcado),
                    aindaPorAtender = SitHoje(SpineApiClient.ScheduleStatus.Agendado)
                                    + SitHoje(SpineApiClient.ScheduleStatus.Confirmado),
                    taxaComparecimento = dHoje.TaxaComparecimento,
                },
                amanha = new
                {
                    data = amanha.ToString("yyyy-MM-dd"),
                    avaliacoesAgendadas = (dAmanha?.Total ?? 0) - SitAmanha(SpineApiClient.ScheduleStatus.Desmarcado),
                },
            });
        }
        catch (SpineApiException ex)
        {
            _logger.LogWarning(ex, "Resumo do dia falhou (unidade {UnitId})", unitId);
            return StatusCode(StatusCodes.Status502BadGateway, new { message = ex.Motivo });
        }
    }
}
