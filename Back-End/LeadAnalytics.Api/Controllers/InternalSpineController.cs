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
    SpineTokenStore tokens,
    SpineApiClient api,
    InternalApiKeyGuard guard,
    ILogger<InternalSpineController> logger) : ControllerBase
{
    private readonly AppDbContext _db = db;
    private readonly SpineAvaliacoesService _avaliacoes = avaliacoes;
    private readonly SpineHistoricoService _historico = historico;
    private readonly SpineTokenStore _tokens = tokens;
    private readonly SpineApiClient _api = api;
    private readonly InternalApiKeyGuard _guard = guard;
    private readonly ILogger<InternalSpineController> _logger = logger;

    /// <summary>
    /// Diagnóstico: LISTA os tratamentos que o card conta, com as datas de cada um.
    ///
    /// Existe porque "o card diz 3 e a tela da franquia diz 0" não se resolve olhando
    /// código: precisa ver QUAIS são os três e por que a franquia não os mostra. Sem
    /// isto a investigação vira tentativa e erro em produção — e o cache de 5 minutos
    /// esconde o log da chamada, então nem o log ajuda.
    ///
    /// Só leitura, protegido pela mesma X-Admin-Key do resto do controller.
    /// </summary>
    [HttpGet("tratamentos/diagnostico")]
    public async Task<IActionResult> TratamentosDiagnostico(
        [FromHeader(Name = "X-Admin-Key")] string? adminKey,
        [FromQuery] int unitId,
        [FromQuery] DateOnly de,
        [FromQuery] DateOnly ate,
        CancellationToken ct = default)
    {
        if (!await _guard.IsAuthorizedAsync(adminKey))
            return Unauthorized(new { message = "Acesso negado" });

        var token = await _tokens.GetTokenAsync(unitId, ct);
        if (token is null) return Ok(new { unitId, conectado = false });

        // Chama a rota direto, sem passar pelo cache do FranquiaTratamentosService:
        // o objetivo é ver o dado de agora, não o que ficou guardado.
        var linhas = await _api.SearchTreatmentsAsync(token, de, ate, ct);

        return Ok(new
        {
            unitId,
            de,
            ate,
            total = linhas.Count,
            valorTotal = linhas.Sum(x => x.Price ?? 0m),
            semPreco = linhas.Count(x => x.Price is null or 0m),
            tratamentos = linhas.Select(t => new
            {
                t.IdTreatment,
                paciente = t.ClientName,
                price = t.Price,
                situacao = t.StatusName,
                created = t.Created,
                createdDiaLocal = t.Created is null ? null : SpineApiClient.DiaLocal(t.Created.Value).ToString("yyyy-MM-dd"),
                dateBegin = t.DateBegin,
                dateBeginDiaLocal = t.DateBegin is null ? null : SpineApiClient.DiaLocal(t.DateBegin.Value).ToString("yyyy-MM-dd"),
            }),
        });
    }

    /// <summary>
    /// Reconciliação tratamento (franquia) × lead (Kommo), casando por TELEFONE.
    ///
    /// O tratamento não traz telefone — traz idClient. O caminho é
    /// tratamento → ficha do paciente (/api/clients/{id}, que tem whatsapp) → lead da
    /// Kommo pelo telefone normalizado. Nome não serve: em Araguaína, 17 de 30 leads
    /// têm a DATA da consulta escrita dentro do campo do nome.
    ///
    /// Devolve os dois valores lado a lado para dizer, sem adivinhar, se as duas bases
    /// falam do mesmo paciente e do mesmo dinheiro.
    /// </summary>
    [HttpGet("reconciliacao")]
    public async Task<IActionResult> Reconciliacao(
        [FromHeader(Name = "X-Admin-Key")] string? adminKey,
        [FromQuery] int unitId,
        [FromQuery] DateOnly de,
        [FromQuery] DateOnly ate,
        CancellationToken ct = default)
    {
        if (!await _guard.IsAuthorizedAsync(adminKey))
            return Unauthorized(new { message = "Acesso negado" });

        var token = await _tokens.GetTokenAsync(unitId, ct);
        if (token is null) return Ok(new { unitId, conectado = false });

        // O campo de valor do tratamento na Kommo é o mesmo que o KPI de receita usa.
        var cfg = await _db.KpiConfigurations.AsNoTracking()
            .Where(k => k.UnitId == unitId && k.KpiKey == "receita")
            .Select(k => k.ConfigJson).FirstOrDefaultAsync(ct);
        long? campoValor = null;
        if (!string.IsNullOrWhiteSpace(cfg))
        {
            using var doc = System.Text.Json.JsonDocument.Parse(cfg);
            if (doc.RootElement.TryGetProperty("fieldId", out var fid) && fid.TryGetInt64(out var v))
                campoValor = v;
        }

        var trats = await _api.SearchTreatmentsAsync(token, de, ate, ct);
        var linhas = new List<LinhaReconciliacao>();

        foreach (var t in trats)
        {
            var ficha = await _api.GetClientAsync(token, t.IdClient, ct);
            var fone = ContactImportService.NormalizePhone(ficha?.Whatsapp ?? "") ?? "";
            var ult8 = fone.Length >= 8 ? fone[^8..] : fone;

            string? leadNome = null, valorKommo = null;
            if (ult8.Length >= 8)
            {
                var lead = await _db.Leads.AsNoTracking()
                    .Where(l => l.UnitId == unitId && l.Phone != null && l.Phone.Contains(ult8))
                    .Select(l => new { l.Name, l.CustomFieldsJson })
                    .FirstOrDefaultAsync(ct);
                if (lead is not null)
                {
                    leadNome = lead.Name;
                    if (campoValor is not null && lead.CustomFieldsJson is not null)
                        valorKommo = KpiConfigService.ExtractFieldValue(lead.CustomFieldsJson, campoValor, null);
                }
            }

            linhas.Add(new LinhaReconciliacao(
                t.IdTreatment,
                t.ClientName,
                string.IsNullOrEmpty(fone) ? null : "…" + ult8,
                t.Price,
                leadNome,
                valorKommo,
                leadNome is not null));
        }

        return Ok(new
        {
            unitId, de, ate,
            tratamentos = trats.Count,
            comTelefone = linhas.Count(x => x.Whatsapp is not null),
            casaramComLead = linhas.Count(x => x.Casou),
            linhas,
        });
    }

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

/// <summary>Uma linha da reconciliação: o mesmo paciente visto pelos dois sistemas.</summary>
public record LinhaReconciliacao(
    long IdTreatment,
    string? Paciente,
    string? Whatsapp,
    decimal? PrecoFranquia,
    string? LeadKommo,
    string? ValorKommo,
    bool Casou);
