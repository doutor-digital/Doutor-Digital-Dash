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
    DatacaoDeMigracaoService datacao,
    InternalApiKeyGuard guard,
    ILogger<InternalSpineController> logger) : ControllerBase
{
    private readonly AppDbContext _db = db;
    private readonly SpineAvaliacoesService _avaliacoes = avaliacoes;
    private readonly SpineHistoricoService _historico = historico;
    private readonly SpineTokenStore _tokens = tokens;
    private readonly SpineApiClient _api = api;
    private readonly KommoApiClient _kommo = kommo;
    private readonly DatacaoDeMigracaoService _datacao = datacao;
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

        // Grava o vínculo: é o que permite ao KPI somar depois sem refazer 44 chamadas.
        foreach (var l in linhas)
        {
            var dia = trats.First(x => x.IdTreatment == l.IdTreatment).Created is { } c
                ? SpineApiClient.DiaLocal(c)
                : DateOnly.FromDateTime(DateTime.UtcNow);

            var existente = await _db.FranquiaLeadLinks
                .FirstOrDefaultAsync(x => x.UnitId == unitId && x.IdTreatment == l.IdTreatment, ct);
            var vk = decimal.TryParse(l.ValorKommo, out var pv) ? pv : (decimal?)null;

            if (existente is null)
            {
                _db.FranquiaLeadLinks.Add(new Models.FranquiaLeadLink
                {
                    UnitId = unitId, IdTreatment = l.IdTreatment, DiaLancamento = dia,
                    Paciente = l.Paciente, Telefone = l.Whatsapp?.TrimStart('…'),
                    PrecoFranquia = l.PrecoFranquia, LeadId = l.LeadId, ValorKommo = vk,
                    AtualizadoEm = DateTime.UtcNow,
                });
            }
            else
            {
                existente.DiaLancamento = dia;
                existente.Paciente = l.Paciente;
                existente.Telefone = l.Whatsapp?.TrimStart('…');
                existente.PrecoFranquia = l.PrecoFranquia;
                // Só sobrescreve o lead/valor quando a Kommo respondeu: um 401 não pode
                // apagar um vínculo bom gravado antes.
                if (l.LeadId is not null) existente.LeadId = l.LeadId;
                if (vk is not null) existente.ValorKommo = vk;
                existente.AtualizadoEm = DateTime.UtcNow;
            }
        }
        // Registra ATÉ ONDE o cruzamento já olhou nesta unidade.
        //
        // Sem isto não dá para diferenciar "não teve tratamento no período" (receita R$ 0,
        // verdade) de "o cruzamento nunca rodou aqui" (não sabemos, e o card tem de dizer
        // isso). No dia 1º de cada mês as duas situações são idênticas no banco — zero
        // linhas — e mostrar "—" num mês que legitimamente começou zerado assusta à toa.
        var chaveCobertura = $"cruzamento:cobertura:{unitId}";
        var marca = await _db.AppConfigurations.FirstOrDefaultAsync(c => c.Key == chaveCobertura, ct);
        var primeiro = de;
        var ultimo = ate;
        if (marca is not null && !string.IsNullOrWhiteSpace(marca.Value))
        {
            var partes = marca.Value.Split('|');
            if (partes.Length == 2
                && DateOnly.TryParse(partes[0], out var pAntigo)
                && DateOnly.TryParse(partes[1], out var uAntigo))
            {
                if (pAntigo < primeiro) primeiro = pAntigo;
                if (uAntigo > ultimo) ultimo = uAntigo;
            }
        }
        var valorCobertura = $"{primeiro:yyyy-MM-dd}|{ultimo:yyyy-MM-dd}";
        if (marca is null)
            _db.AppConfigurations.Add(new Models.AppConfiguration
            {
                Key = chaveCobertura,
                Value = valorCobertura,
                // NOT NULL sem default no banco: sem preencher aqui a linha nem entra.
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            });
        else
        {
            marca.Value = valorCobertura;
            marca.UpdatedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(ct);

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
        [FromQuery] decimal minimoValor = SelecaoDeEscrita.PisoPadrao,
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

        var (alvos, suspeitos) = SelecaoDeEscrita.Separar(linhas, minimoValor);

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
            minimoValor,
            tratamentos = linhas.Count,
            jaPreenchidos = linhas.Count(l => !string.IsNullOrWhiteSpace(l.ValorKommo)),
            semLeadNaKommo = linhas.Count(l => l.LeadId is null),
            alterados = gravados.Count,
            leads = gravados,
            // Preço com cara de dígito faltando: NÃO gravado, e devolvido para alguém
            // corrigir na franquia — lá o número também está errado.
            suspeitos = suspeitos.Select(l => new
            {
                l.LeadId, l.Paciente, l.Whatsapp, precoFranquia = l.PrecoFranquia,
            }),
        });
    }

    /// <summary>
    /// Roda o cruzamento em TODAS as unidades conectadas e grava os vínculos.
    ///
    /// É o que o cron chama uma vez por dia. Cada unidade custa duas chamadas por
    /// tratamento, então elas vão em série, com respiro entre uma e outra: a Kommo
    /// limita requisições e um lote apressado volta 429 no meio.
    ///
    /// Falha de uma unidade não para o lote — ela entra no relatório com o motivo.
    /// Token vencido da Kommo é o caso mais comum, e precisa aparecer por unidade para
    /// alguém saber qual renovar.
    /// </summary>
    [HttpPost("reconciliacao/todas")]
    public async Task<IActionResult> ReconciliarTodas(
        [FromHeader(Name = "X-Admin-Key")] string? adminKey,
        [FromQuery] DateOnly de,
        [FromQuery] DateOnly ate,
        [FromQuery] int pausaMs = 1500,
        CancellationToken ct = default)
    {
        if (!await _guard.IsAuthorizedAsync(adminKey))
            return Unauthorized(new { message = "Acesso negado" });

        var unidades = await _db.Units.AsNoTracking()
            .Where(u => u.IsActive)
            .OrderBy(u => u.Id)
            .Select(u => new { u.Id, u.Slug })
            .ToListAsync(ct);

        var relatorio = new List<object>();
        foreach (var u in unidades)
        {
            try
            {
                var r = await ReconciliarAsync(u.Id, de, ate, ct);
                if (r is null)
                {
                    relatorio.Add(new { u.Id, u.Slug, situacao = "sem token da franquia" });
                    continue;
                }

                var (linhas, _, _, erroKommo) = r.Value;
                relatorio.Add(new
                {
                    u.Id,
                    u.Slug,
                    situacao = erroKommo is null ? "ok" : "kommo falhou",
                    tratamentos = linhas.Count,
                    casaram = linhas.Count(x => x.Casou),
                    semValorNaKommo = linhas.Count(x => x.Casou && string.IsNullOrWhiteSpace(x.ValorKommo)),
                    somaFranquia = linhas.Sum(x => x.PrecoFranquia ?? 0m),
                    erroKommo,
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Reconciliacao em lote: unidade {UnitId} falhou", u.Id);
                relatorio.Add(new { u.Id, u.Slug, situacao = "erro", erro = ex.Message });
            }

            if (pausaMs > 0) await Task.Delay(pausaMs, ct);
        }

        return Ok(new { de, ate, unidades = relatorio.Count, relatorio });
    }


    /// <summary>
    /// Carimba a data REAL nas movimentações de migração retroativa.
    ///
    /// O PROBLEMA
    /// ----------
    /// Quando a SDR arrasta hoje um card que virou tratamento em maio, a Kommo registra
    /// a entrada na etapa com a data de HOJE, e esse carimbo não é editável lá. Todo KPI
    /// que conta por entrada na etapa — receita, semáforo, funil — joga maio dentro de
    /// hoje. Numa migração de centenas de cards, o dia da migração vira o melhor mês da
    /// história da unidade, e os meses reais ficam vazios.
    ///
    /// A SAÍDA
    /// -------
    /// A data verdadeira existe: é o dia em que a franquia lançou o tratamento, que o
    /// cruzamento já guardou em <c>franquia_lead_link</c>. E o modelo já tem
    /// <see cref="Models.LeadStageHistory.CorrectedChangedAt"/>, que os KPIs preferem à
    /// <c>ChangedAt</c> — foi feito para a SDR que move o card no dia seguinte. Aqui a
    /// gente preenche esse campo em lote, com a data da franquia, em vez de pedir para
    /// alguém corrigir card por card.
    ///
    /// NÃO INVENTA DATA: só toca em linha de lead que tem tratamento correspondente na
    /// franquia. Card movido sem tratamento casado fica como está e volta no relatório —
    /// é melhor um número que falta do que um número inventado.
    ///
    /// Correção humana já existente nunca é sobrescrita.
    /// </summary>
    /// <param name="de">Início da janela de LANÇAMENTO na franquia (o mês real do tratamento).</param>
    /// <param name="ate">Fim dessa janela.</param>
    /// <param name="movidoDe">Quando os cards foram arrastados. Padrão: hoje 00:00 UTC.</param>
    /// <param name="movidoAte">Fim do arraste. Padrão: agora.</param>
    [HttpPost("datar-migracao")]
    public async Task<IActionResult> DatarMigracao(
        [FromHeader(Name = "X-Admin-Key")] string? adminKey,
        [FromQuery] int unitId,
        [FromQuery] DateOnly de,
        [FromQuery] DateOnly ate,
        [FromQuery] DateTime? movidoDe = null,
        [FromQuery] DateTime? movidoAte = null,
        [FromQuery] bool aplicar = false,
        CancellationToken ct = default)
    {
        if (!await _guard.IsAuthorizedAsync(adminKey))
            return Unauthorized(new { message = "Acesso negado" });

        // A regra mora no serviço, que é o mesmo usado pela tela da SDR. Duas cópias da
        // regra viram duas datas diferentes para o mesmo card.
        var previa = await _datacao.PreverAsync(unitId, de, ate, movidoDe, movidoAte, ct);

        var corrigidas = aplicar
            ? await _datacao.AplicarAsync(unitId, de, ate, movidoDe, movidoAte, null, null, null, ct)
            : 0;

        return Ok(new
        {
            unitId,
            modo = aplicar ? "GRAVADO" : "simulacao",
            janela = new { de = previa.JanelaDe, ate = previa.JanelaAte },
            lancamentosConsiderados = previa.LeadsComTratamento,
            leadsComMaisDeUmTratamento = previa.LeadsComMaisDeUmTratamento,
            movimentacoesNaJanela = previa.MovimentacoesNaJanela,
            corrigidas = aplicar ? corrigidas : previa.Datar.Count,
            semVinculo = previa.SemVinculo.Count,
            detalhe = previa.Datar.Take(200).Select(m => new
            {
                LeadIdInterno = m.LeadIdInterno, etapa = m.Etapa, etapaId = m.EtapaId,
                arrastadoEm = m.ArrastadoEm, dataReal = m.LancadoEm,
            }),
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

/// <summary>
/// Separa o que pode ser gravado na Kommo do que precisa de olho humano antes.
///
/// POR QUE EXISTE UM PISO
/// ----------------------
/// Medido em 01/09/2026 sobre agosto inteiro, nas 10 unidades com token da franquia:
/// TODO preço abaixo de R$ 1.000 da rede é de Parauapebas — seis de R$ 368 e dois de
/// R$ 420 — e cada um é exatamente um décimo de um valor que a própria unidade
/// pratica (3.680 e 4.200). O menor preço legítimo em qualquer unidade é R$ 1.600.
/// É digitação com um zero a menos.
///
/// Gravar isso trocaria um campo VAZIO por um número ERRADO, e essa troca é ruim:
/// campo vazio ninguém soma, número errado entra na receita e vira decisão. Por isso
/// a linha suspeita não é escrita — ela volta no relatório, para a clínica corrigir
/// na origem, onde o número também está errado.
/// </summary>
public static class SelecaoDeEscrita
{
    /// <summary>Piso padrão em reais. Parametrizável na rota, para o dia em que a rede vender algo barato.</summary>
    public const decimal PisoPadrao = 1000m;

    /// <param name="piso">Abaixo disto a linha é suspeita, não gravada. 0 desliga a trava.</param>
    public static (List<LinhaReconciliacao> Gravar, List<LinhaReconciliacao> Suspeitos) Separar(
        IEnumerable<LinhaReconciliacao> linhas, decimal piso)
    {
        var gravar = new List<LinhaReconciliacao>();
        var suspeitos = new List<LinhaReconciliacao>();

        foreach (var l in linhas)
        {
            // Sem lead casado não há onde gravar; sem preço não há o que gravar; e campo
            // já preenchido nunca é tocado — a rota só completa vazio, não corrige humano.
            if (l.LeadId is null || l.PrecoFranquia is not > 0
                || !string.IsNullOrWhiteSpace(l.ValorKommo))
                continue;

            if (l.PrecoFranquia < piso) suspeitos.Add(l);
            else gravar.Add(l);
        }

        return (gravar, suspeitos);
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
