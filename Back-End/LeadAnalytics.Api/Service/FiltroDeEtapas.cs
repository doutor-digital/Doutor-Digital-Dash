using System.Text.Json;
using LeadAnalytics.Api.Models;

namespace LeadAnalytics.Api.Service;

/// <summary>
/// As etapas que um KPI conta, com a opção de prender uma etapa a um FUNIL.
///
/// POR QUE EXISTE
/// --------------
/// A config do KPI guarda só o número da etapa (<c>stageIds</c>). Para quase todas as
/// etapas isso basta — o número é único na conta. Para 142 (ganho) e 143 (perdido), não:
/// eles existem em TODO funil. O Semáforo da Açailândia pedia o 142 querendo a ALTA do
/// TRATAMENTO e somava junto o GANHO do COMERCIAL.
///
/// A config ganha um campo opcional, sem mexer no <c>stageIds</c> que a tela edita:
/// <code>"funilDaEtapa": { "142": 14304315 }</code>
/// = "a etapa 142 só conta no funil 14304315". Etapa fora do mapa segue valendo em
/// qualquer funil, como sempre.
///
/// Linha sem funil gravado (histórico antigo, lead que ainda não recebeu webhook depois da
/// coluna de funil) NÃO conta para uma etapa presa: não dá para saber de qual funil ela é,
/// e contar seria repetir o erro que isto conserta.
/// </summary>
public sealed class FiltroDeEtapas
{
    /// <summary>
    /// Par (funil, etapa) num número só — <c>funil × 10⁹ + etapa</c> — para o filtro virar
    /// um <c>= ANY(...)</c> no SQL. Ids de etapa da Kommo têm 9 dígitos no máximo e os de
    /// funil 8, então o resultado cabe com folga num <c>long</c>.
    /// </summary>
    public const long FatorFunil = 1_000_000_000L;

    /// <summary>Etapas que valem em qualquer funil.</summary>
    public List<int> Livres { get; }

    /// <summary>Etapas presas a um funil, já codificadas (<see cref="FatorFunil"/>).</summary>
    public List<long> Pares { get; }

    public int Count => Livres.Count + Pares.Count;

    public FiltroDeEtapas(IEnumerable<int> etapas, IReadOnlyDictionary<int, long>? funilDaEtapa = null)
    {
        var presas = funilDaEtapa ?? new Dictionary<int, long>();
        Livres = new List<int>();
        Pares = new List<long>();
        foreach (var etapa in etapas.Distinct())
        {
            if (presas.TryGetValue(etapa, out var funil)) Pares.Add(Codificar(funil, etapa));
            else Livres.Add(etapa);
        }
    }

    public static long Codificar(long funil, long etapa) => funil * FatorFunil + etapa;

    /// <summary>Lê <c>"funilDaEtapa": { "142": 14304315 }</c> da config. Ausente ou inválido → vazio.</summary>
    public static Dictionary<int, long> LerFunilDaEtapa(JsonElement config)
    {
        var mapa = new Dictionary<int, long>();
        if (config.ValueKind != JsonValueKind.Object
            || !config.TryGetProperty("funilDaEtapa", out var el)
            || el.ValueKind != JsonValueKind.Object)
            return mapa;

        foreach (var p in el.EnumerateObject())
        {
            if (!int.TryParse(p.Name, out var etapa)) continue;
            long funil;
            if (p.Value.ValueKind == JsonValueKind.Number && p.Value.TryGetInt64(out var n)) funil = n;
            else if (p.Value.ValueKind == JsonValueKind.String && long.TryParse(p.Value.GetString(), out var s)) funil = s;
            else continue;
            mapa[etapa] = funil;
        }
        return mapa;
    }

    /// <summary>Entradas do histórico nas etapas do filtro.</summary>
    public IQueryable<LeadStageHistory> Aplicar(IQueryable<LeadStageHistory> q)
    {
        var livres = Livres;
        if (Pares.Count == 0) return q.Where(h => livres.Contains(h.StageId));
        var pares = Pares;
        return q.Where(h => livres.Contains(h.StageId)
            || (h.PipelineId != null && pares.Contains(h.PipelineId.Value * FatorFunil + h.StageId)));
    }

    /// <summary>Leads cuja etapa ATUAL está no filtro.</summary>
    public IQueryable<Lead> AplicarNaEtapaAtual(IQueryable<Lead> q)
    {
        var livres = Livres;
        if (Pares.Count == 0)
            return q.Where(l => l.CurrentStageId != null && livres.Contains(l.CurrentStageId.Value));
        var pares = Pares;
        return q.Where(l => l.CurrentStageId != null
            && (livres.Contains(l.CurrentStageId.Value)
                || (l.CurrentPipelineId != null
                    && pares.Contains(l.CurrentPipelineId.Value * FatorFunil + l.CurrentStageId.Value))));
    }
}
