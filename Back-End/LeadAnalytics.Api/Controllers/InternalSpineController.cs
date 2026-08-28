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
    KommoApiClient kommo,
    InternalApiKeyGuard guard,
    ILogger<InternalSpineController> logger) : ControllerBase
{
    private readonly AppDbContext _db = db;
    private readonly SpineAvaliacoesService _avaliacoes = avaliacoes;
    private readonly SpineHistoricoService _historico = historico;
    private readonly SpineTokenStore _tokens = tokens;
    private readonly SpineApiClient _api = api;
    private readonly KommoApiClient _kommo = kommo;
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
    /// <summary>
    /// O cruzamento em si: tratamentos da franquia no período, com o lead da Kommo
    /// encontrado por telefone e o valor que cada lado registra. Compartilhado pela
    /// consulta e pelo preenchimento — duas rotas com a mesma verdade.
    /// </summary>
    private async Task<(List<LinhaReconciliacao> Linhas, long? CampoValor,
        Models.Unit? Unidade, string? ErroKommo)?> ReconciliarAsync(
        int unitId, DateOnly de, DateOnly ate, CancellationToken ct)
    {
        var token = await _tokens.GetTokenAsync(unitId, ct);
        if (token is null) return null;

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

        var unidade = await _db.Units.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == unitId, ct);

        var trats = await _api.SearchTreatmentsAsync(token, de, ate, ct);
        var linhas = new List<LinhaReconciliacao>();
        string? erroKommo = null;

        foreach (var t in trats)
        {
            var ficha = await _api.GetClientAsync(token, t.IdClient, ct);
            var fone = ContactImportService.NormalizePhone(ficha?.Whatsapp ?? "") ?? "";
            var ult8 = fone.Length >= 8 ? fone[^8..] : fone;

            string? leadNome = null, valorKommo = null;
            long? leadId = null;
            if (ult8.Length >= 8 && unidade is not null
                && !string.IsNullOrWhiteSpace(unidade.KommoSubdomain)
                && !string.IsNullOrWhiteSpace(unidade.KommoAccessToken))
            {
                // Busca na KOMMO, não no espelho: a coluna Phone do nosso banco está
                // vazia em todas as unidades, então procurar aqui dava sempre zero.
                try
                {
                    var achados = await _kommo.SearchLeadsAsync(
                        unidade.KommoSubdomain, unidade.KommoAccessToken, ult8, ct);
                    var lead = achados?.Embedded?.Leads?.FirstOrDefault();
                    if (lead is not null)
                    {
                        leadId = lead.Id;
                        leadNome = lead.Name;
                        var campo = lead.CustomFieldsValues?
                            .FirstOrDefault(f => campoValor is not null && f.FieldId == campoValor);
                        valorKommo = campo?.Values?.FirstOrDefault()?.Value?.ToString();
                    }
                }
                catch (Exception ex)
                {
                    // Token da Kommo vencido derrubava a requisição inteira e a lista dos
                    // pacientes da franquia — que não depende da Kommo — ia junto. Uma
                    // ponta quebrada não pode apagar a outra.
                    erroKommo ??= ex.Message.Length > 160 ? ex.Message[..160] : ex.Message;
                }
            }

            linhas.Add(new LinhaReconciliacao(
                t.IdTreatment,
                leadId,
                t.ClientName,
                string.IsNullOrEmpty(fone) ? null : "…" + ult8,
                t.Price,
                leadNome,
                valorKommo,
                leadNome is not null));
        }

        return (linhas, campoValor, unidade, erroKommo);
    }

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

        var r = await ReconciliarAsync(unitId, de, ate, ct);
        if (r is null) return Ok(new { unitId, conectado = false });

        return Ok(new
        {
            unitId, de, ate,
            tratamentos = r.Value.Linhas.Count,
            erroKommo = r.Value.ErroKommo,
            comTelefone = r.Value.Linhas.Count(x => x.Whatsapp is not null),
            casaramComLead = r.Value.Linhas.Count(x => x.Casou),
            linhas = r.Value.Linhas,
        });
    }

    /// <summary>
    /// Preenche na Kommo o valor do tratamento dos leads que fecharam e estão com o
    /// campo em branco, usando o preço que a franquia registrou.
    ///
    /// NASCE EM SIMULAÇÃO. Sem <c>aplicar=true</c> nada é gravado: devolve a lista do
    /// que MUDARIA. Escrever em ficha de paciente é irreversível pela API, então a
    /// ordem é ver primeiro, gravar depois.
    ///
    /// Só toca em campo VAZIO. Onde a Kommo já tem valor, respeita o que está lá —
    /// mesmo divergente — porque sobrescrever apagaria uma correção manual sem deixar
    /// rastro.
    /// </summary>
    [HttpPost("reconciliacao/preencher")]
    public async Task<IActionResult> PreencherValores(
        [FromHeader(Name = "X-Admin-Key")] string? adminKey,
        [FromQuery] int unitId,
        [FromQuery] DateOnly de,
        [FromQuery] DateOnly ate,
        [FromQuery] bool aplicar = false,
        CancellationToken ct = default)
    {
        if (!await _guard.IsAuthorizedAsync(adminKey))
            return Unauthorized(new { message = "Acesso negado" });

        var resultado = await ReconciliarAsync(unitId, de, ate, ct);
        if (resultado is null) return Ok(new { unitId, conectado = false });
        var (linhas, campoValor, unidade, erroKommo) = resultado.Value;

        if (campoValor is null)
            return BadRequest(new { message = "Unidade sem campo de valor mapeado no KPI de receita." });
        if (unidade is null || string.IsNullOrWhiteSpace(unidade.KommoSubdomain)
            || string.IsNullOrWhiteSpace(unidade.KommoAccessToken))
            return BadRequest(new { message = "Unidade sem credencial da Kommo." });

        var alvos = linhas
            .Where(l => l.LeadId is not null
                        && l.PrecoFranquia is > 0
                        && string.IsNullOrWhiteSpace(l.ValorKommo))
            .ToList();

        var gravados = new List<object>();
        foreach (var l in alvos)
        {
            var valor = ((int)Math.Round(l.PrecoFranquia!.Value)).ToString();
            if (aplicar)
            {
                await _kommo.PatchLeadCustomFieldsAsync(
                    unidade.KommoSubdomain, unidade.KommoAccessToken, l.LeadId!.Value,
                    new[] { new KommoCustomFieldPatch(campoValor.Value, "numeric", valor, null) }, ct);
                _logger.LogInformation(
                    "Reconciliacao: gravado valor {Valor} no lead {LeadId} (unidade {UnitId}, paciente {Paciente})",
                    valor, l.LeadId, unitId, l.Paciente);
            }
            gravados.Add(new { l.LeadId, l.Paciente, l.Whatsapp, valor });
        }

        return Ok(new
        {
            unitId, de, ate,
            modo = aplicar ? "GRAVADO" : "simulacao",
            erroKommo,
            tratamentos = linhas.Count,
            jaPreenchidos = linhas.Count(l => !string.IsNullOrWhiteSpace(l.ValorKommo)),
            semLeadNaKommo = linhas.Count(l => l.LeadId is null),
            alterados = gravados.Count,
            leads = gravados,
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
    long? LeadId,
    string? Paciente,
    string? Whatsapp,
    decimal? PrecoFranquia,
    string? LeadKommo,
    string? ValorKommo,
    bool Casou);
