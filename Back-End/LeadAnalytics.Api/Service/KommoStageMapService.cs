using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.Models;
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
    public sealed record ResultadoBackfill(
        int UnitId, int Examinados, int Corrigidos, int SemMapa, int Ambiguos);

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
        if (etapas.Count == 0) return new ResultadoBackfill(unitId, 0, 0, 0, 0);

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

        int corrigidos = 0, semMapa = 0, ambiguos = 0;
        foreach (var h in linhas)
        {
            string? nome = null;
            if (h.PipelineId is long fid && porFunilEtapa.TryGetValue((fid, h.StageId), out var exato))
                nome = exato;
            else if (porEtapaUnica.TryGetValue(h.StageId, out var unico))
                nome = unico;
            else if (etapas.Any(s => s.StatusId == h.StageId))
                ambiguos++; // existe no mapa, mas em mais de um funil com nomes diferentes

            if (string.IsNullOrWhiteSpace(nome))
            {
                if (h.StageLabel is null || System.Text.RegularExpressions.Regex.IsMatch(h.StageLabel, "^[0-9]+$"))
                    semMapa++;
                continue;
            }
            if (!string.Equals(h.StageLabel, nome, StringComparison.Ordinal))
            {
                if (!simular) h.StageLabel = nome;
                corrigidos++;
            }
        }

        if (!simular && corrigidos > 0) await db.SaveChangesAsync(ct);
        logger.LogInformation(
            "🏷️ Rótulos do histórico | unidade={Unit} examinados={Ex} corrigidos={Corr} sem mapa={Sem} ambíguos={Amb}{Sim}",
            unitId, linhas.Count, corrigidos, semMapa, ambiguos, simular ? " (simulação)" : "");
        return new ResultadoBackfill(unitId, linhas.Count, corrigidos, semMapa, ambiguos);
    }
}
