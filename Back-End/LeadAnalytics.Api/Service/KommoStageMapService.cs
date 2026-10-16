using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.Models;
using LeadAnalytics.Api.Service.Stages;
using Microsoft.EntityFrameworkCore;

namespace LeadAnalytics.Api.Service;

/// <summary>
/// Mantém o mapa id→nome das etapas de cada conta e conserta o histórico que
/// ficou com o rótulo podre.
///
/// A REGRA: o histórico guarda o ID (estável); o NOME se resolve na leitura.
/// Persistir nome é o que produziu 202 rótulos para 12 etapas e 22% de registros
/// com número no lugar do nome.
/// </summary>
public sealed class KommoStageMapService(
    AppDbContext db,
    KommoApiClient kommo,
    ILogger<KommoStageMapService> logger)
{
    public sealed record ResultadoSync(int UnitId, int Etapas, int Funis, string? Erro = null);
    /// <param name="SemMapa">Rótulo continua cru: o mapa não resolveu o id.</param>
    /// <param name="Ambiguos">Id existe no mapa, mas em mais de um funil com nomes diferentes.</param>
    /// <param name="AmbiguosComIdCru">
    /// O subconjunto que importa: ambíguo E ainda mostrando o número na tela. É o que a
    /// coluna PipelineId recupera. O resto do SemMapa aponta para etapa que não existe
    /// mais na Kommo (apagada nas replicações) — esse nome não volta de lugar nenhum.
    /// </param>
    public sealed record ResultadoBackfill(
        int UnitId, int Examinados, int Corrigidos, int SemMapa, int Ambiguos, int AmbiguosComIdCru);

    /// <summary>Lê os funis da conta e regrava o mapa da unidade.</summary>
    public async Task<ResultadoSync> SincronizarAsync(int unitId, CancellationToken ct)
    {
        var unit = await db.Units.AsNoTracking().FirstOrDefaultAsync(u => u.Id == unitId, ct);
        if (unit is null) return new ResultadoSync(unitId, 0, 0, "unidade não encontrada");
        if (string.IsNullOrWhiteSpace(unit.KommoSubdomain) || string.IsNullOrWhiteSpace(unit.KommoAccessToken))
            return new ResultadoSync(unitId, 0, 0, "unidade sem credencial da Kommo");

        KommoPipelinesResponse? resp;
        try
        {
            resp = await kommo.GetPipelinesAsync(unit.KommoSubdomain!, unit.KommoAccessToken!, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "mapa de etapas: falha ao ler funis da unidade {Unit}", unitId);
            return new ResultadoSync(unitId, 0, 0, ex.Message);
        }

        var pipelines = resp?.Embedded?.Pipelines ?? [];
        if (pipelines.Count == 0) return new ResultadoSync(unitId, 0, 0, "conta sem funis");

        var atuais = await db.KommoStages.Where(s => s.UnitId == unitId).ToListAsync(ct);
        var agora = DateTime.UtcNow;
        var vistos = new HashSet<(long, long)>();

        foreach (var p in pipelines)
        {
            foreach (var st in p.Embedded?.Statuses ?? [])
            {
                vistos.Add((p.Id, st.Id));
                var linha = atuais.FirstOrDefault(x => x.PipelineId == p.Id && x.StatusId == st.Id);
                if (linha is null)
                {
                    db.KommoStages.Add(new KommoStage
                    {
                        UnitId = unitId, PipelineId = p.Id, PipelineName = p.Name ?? "",
                        StatusId = st.Id, StatusName = st.Name ?? "", Sort = st.Sort, UpdatedAt = agora,
                    });
                }
                else
                {
                    linha.PipelineName = p.Name ?? "";
                    linha.StatusName = st.Name ?? "";
                    linha.Sort = st.Sort;
                    linha.UpdatedAt = agora;
                }
            }
        }

        // Etapa apagada na Kommo continua no mapa: o histórico antigo aponta pra
        // ela e sem a linha o nome viraria número de novo.
        await db.SaveChangesAsync(ct);
        var total = await db.KommoStages.CountAsync(s => s.UnitId == unitId, ct);
        logger.LogInformation("🗺️ Mapa de etapas | unidade={Unit} funis={Funis} etapas={Etapas}", unitId, pipelines.Count, total);
        return new ResultadoSync(unitId, total, pipelines.Count);
    }

    /// <summary>
    /// Reescreve o <c>StageLabel</c> do histórico usando o mapa — para os
    /// registros que ficaram com o id cru ou com nome de uma nomenclatura antiga.
    /// O id nunca é tocado: ele é a verdade.
    /// </summary>
    public async Task<ResultadoBackfill> CorrigirRotulosAsync(
        int unitId, int dias, bool simular, CancellationToken ct)
    {
        var etapas = await db.KommoStages.AsNoTracking()
            .Where(s => s.UnitId == unitId)
            .ToListAsync(ct);
        if (etapas.Count == 0) return new ResultadoBackfill(unitId, 0, 0, 0, 0, 0);

        // Chave exata: (funil, etapa). É a única que não confunde os ids universais
        // 142/143 (Ganho/Perdido), que existem em todos os funis da conta.
        var porFunilEtapa = etapas
            .GroupBy(s => (s.PipelineId, s.StatusId))
            .ToDictionary(g => g.Key, g => g.First().StatusName);

        // Fallback só para linhas sem funil (legado e mudanças pelo painel): vale quando
        // o id da etapa tem UM nome só na conta inteira. Se tem dois, reescrever seria
        // escolher no chute — melhor deixar como está e contar como ambíguo.
        var porEtapaUnica = etapas
            .GroupBy(s => s.StatusId)
            .Where(g => g.Select(s => s.StatusName).Distinct(StringComparer.Ordinal).Count() == 1)
            .ToDictionary(g => g.Key, g => g.First().StatusName);

        var desde = DateTime.UtcNow.AddDays(-dias);
        var linhas = await db.LeadStageHistories
            .Where(h => h.ChangedAt >= desde && db.Leads.Any(l => l.Id == h.LeadId && l.UnitId == unitId))
            .ToListAsync(ct);

        var idsDoMapa = etapas.Select(s => s.StatusId).ToHashSet();

        int corrigidos = 0, semMapa = 0, ambiguos = 0, ambiguosCrus = 0;
        foreach (var h in linhas)
        {
            string? nome = null;
            var ambiguo = false;
            if (h.PipelineId is long fid && porFunilEtapa.TryGetValue((fid, h.StageId), out var exato))
                nome = exato;
            else if (porEtapaUnica.TryGetValue(h.StageId, out var unico))
                nome = unico;
            else if (idsDoMapa.Contains(h.StageId))
            {
                ambiguo = true; // existe no mapa, mas em mais de um funil com nomes diferentes
                ambiguos++;
            }

            if (string.IsNullOrWhiteSpace(nome))
            {
                if (h.StageLabel is null || System.Text.RegularExpressions.Regex.IsMatch(h.StageLabel, "^[0-9]+$"))
                {
                    semMapa++;
                    if (ambiguo) ambiguosCrus++;
                }
                continue;
            }

            // O StageLabel é o rótulo CANÔNICO (04_AGENDADO_SEM_PAGAMENTO), não o nome que
            // aparece na Kommo ("AGENDADO - SEM PAGAMENTO"). Gravar o nome cru faria a coluna
            // parar de casar com LeadStages.* e zeraria os KPIs. Então o mapa dá o nome e o
            // canonicalizador dá o rótulo.
            var canonico = CanonicalStages.ToLeadStage(CanonicalStages.Resolve(nome));
            if (canonico is null)
            {
                semMapa++; // etapa fora do funil canônico — não é rastreada
                continue;
            }
            if (!string.Equals(h.StageLabel, canonico, StringComparison.Ordinal))
            {
                if (!simular) h.StageLabel = canonico;
                corrigidos++;
            }
        }

        if (!simular && corrigidos > 0) await db.SaveChangesAsync(ct);
        logger.LogInformation(
            "🏷️ Rótulos do histórico | unidade={Unit} examinados={Ex} corrigidos={Corr} sem mapa={Sem} ambíguos crus={Amb}{Sim}",
            unitId, linhas.Count, corrigidos, semMapa, ambiguosCrus, simular ? " (simulação)" : "");
        return new ResultadoBackfill(unitId, linhas.Count, corrigidos, semMapa, ambiguos, ambiguosCrus);
    }

    /// <param name="SemFunil">
    /// Etapa ambígua (142/143) sem funil conhecido — nem na coluna do lead, nem no histórico.
    /// Fica como está: quem resolve é o sync da unidade, que lê o funil direto da Kommo.
    /// </param>
    /// <param name="Mudancas">"de → para" com a quantidade, para conferir antes de gravar.</param>
    /// <param name="Amostra">Até 20 ids da Kommo (cartões) que seriam corrigidos.</param>
    /// <param name="FunisPreenchidos">
    /// Leads com a coluna de funil vazia que ganham o funil (porque o número da etapa só
    /// existe em um funil) — mesmo quando a etapa já estava certa. Sem
    /// isso, um lead parado nunca recebe o funil e fica fora de KPI com etapa presa a funil.
    /// </param>
    public sealed record ResultadoEtapaAtual(
        int UnitId, int Examinados, int Corrigidos, int SemFunil,
        IReadOnlyList<MudancaDeEtapa> Mudancas, IReadOnlyList<int> Amostra,
        int FunisPreenchidos = 0, string? Erro = null);

    public sealed record MudancaDeEtapa(string De, string Para, int Quantidade);

    /// <summary>
    /// Conserta a ETAPA ATUAL (<see cref="Lead.CurrentStage"/>) dos leads gravados com a
    /// etapa canônica errada — o caso dos perdidos do COMERCIAL gravados como "tratamento
    /// cancelado" porque o webhook traduzia o 143 sem olhar o funil.
    ///
    /// De onde vem o funil de cada lead: da coluna <see cref="Lead.CurrentPipelineId"/> (o sync
    /// grava direto da Kommo), ou do número da etapa quando ele só existe em um funil. Nunca do
    /// histórico, que tem buracos (review de 16/10). Sem funil conhecido, o lead não é tocado e
    /// entra em <c>SemFunil</c> — rode o sync da unidade antes. O histórico nunca é alterado aqui (isso é o
    /// <see cref="CorrigirRotulosAsync"/>), e o id da etapa também não.
    ///
    /// Só mexe no defeito que a auditoria achou: etapa de número AMBÍGUO na conta (142/143)
    /// ou etapa gravada como o número cru (ex.: EM ESPERA = "111237819"). Etapa de número
    /// único com rótulo diferente do mapa fica como está — pode ser ajuste manual do painel.
    ///
    /// <paramref name="simular"/> = true (padrão no endpoint) só conta e mostra; não grava.
    /// </summary>
    public async Task<ResultadoEtapaAtual> CorrigirEtapaAtualAsync(int unitId, bool simular, CancellationToken ct)
    {
        var etapas = await db.KommoStages.AsNoTracking()
            .Where(s => s.UnitId == unitId)
            .Select(s => new EtapaDaConta(s.PipelineId, s.StatusId, s.StatusName))
            .ToListAsync(ct);
        if (etapas.Count == 0)
            return new ResultadoEtapaAtual(unitId, 0, 0, 0, [], [],
                Erro: "unidade sem mapa de etapas — rode antes POST /internal/stage-map/sync?unitId=" + unitId);

        var leads = await db.Leads
            .Where(l => l.UnitId == unitId && l.CurrentStageId != null && l.Status != LeadQueryExtensions.StatusDeleted)
            .ToListAsync(ct);

        // NÃO se deduz o funil pelo histórico (review de 16/10): o sync grava sem histórico e as linhas de webhook
        // antes de 04/09 não têm funil, então "a última linha com o mesmo número" pode ser o 142 do COMERCIAL de
        // um lead que hoje está no 142 do TRATAMENTO — e o conserto gravaria ALTA como "fechou tratamento". Sem
        // funil conhecido o lead fica como está (SemFunil); quem resolve é o sync da unidade, que lê o funil
        // direto da Kommo — por isso o script roda o sync ANTES deste passo.

        // Em quantos funis cada número de etapa existe. 142/143 existem em todos; o resto,
        // em um só — e aí o número basta para saber o funil.
        var funisDoNumero = etapas.GroupBy(e => e.Etapa)
            .ToDictionary(g => g.Key, g => g.Select(e => e.Funil).Distinct().ToList());

        int corrigidos = 0, semFunil = 0, funisPreenchidos = 0;
        var mudancas = new Dictionary<(string De, string Para), int>();
        var amostra = new List<int>();

        foreach (var lead in leads)
        {
            var etapa = lead.CurrentStageId!.Value;
            var funisPossiveis = funisDoNumero.GetValueOrDefault(etapa) ?? [];
            long? funil = lead.CurrentPipelineId
                ?? (funisPossiveis.Count == 1 ? (long?)funisPossiveis[0] : null);

            if (lead.CurrentPipelineId is null && funil is not null)
            {
                funisPreenchidos++;
                if (!simular) lead.CurrentPipelineId = funil;
            }

            var nome = MapaCanonicoDeEtapas.Nome(etapas, funil, etapa);
            if (nome is null)
            {
                // Só conta como "sem funil" quando o número é ambíguo na conta. Etapa que
                // não existe mais no mapa não tem nome de onde voltar.
                if (funil is null && funisPossiveis.Count > 1) semFunil++;
                continue;
            }

            var ambigua = funisPossiveis.Count > 1;
            var numeroCru = string.Equals(lead.CurrentStage, etapa.ToString(), StringComparison.Ordinal);
            if (!ambigua && !numeroCru) continue; // fora do defeito — pode ser ajuste manual

            var esperado = CanonicalStages.ToLeadStage(CanonicalStages.Resolve(nome));
            if (esperado is null) continue; // etapa fora do funil canônico — fica como está
            if (string.Equals(lead.CurrentStage, esperado, StringComparison.Ordinal)) continue;

            var chave = (lead.CurrentStage, esperado);
            mudancas[chave] = mudancas.GetValueOrDefault(chave) + 1;
            if (amostra.Count < 20) amostra.Add(lead.ExternalId);
            corrigidos++;

            if (!simular) lead.CurrentStage = esperado;
        }

        if (!simular && (corrigidos > 0 || funisPreenchidos > 0)) await db.SaveChangesAsync(ct);
        logger.LogInformation(
            "🩹 Etapa atual | unidade={Unit} examinados={Ex} corrigidos={Corr} sem funil={Sem} funis preenchidos={Fun}{Sim}",
            unitId, leads.Count, corrigidos, semFunil, funisPreenchidos, simular ? " (simulação)" : "");

        return new ResultadoEtapaAtual(unitId, leads.Count, corrigidos, semFunil,
            mudancas.Select(kv => new MudancaDeEtapa(kv.Key.De, kv.Key.Para, kv.Value))
                .OrderByDescending(m => m.Quantidade).ToList(),
            amostra, funisPreenchidos);
    }
}
