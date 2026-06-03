namespace LeadAnalytics.Api.Models;

/// <summary>
/// Mapeamento configurável de UM KPI do dashboard para a sua fonte de dados, definido
/// pelo analista de TI nas "Configurações Técnicas". Cada linha diz, para uma unidade,
/// de onde o número daquele KPI deve sair (ex.: o KPI "resgate" conta os leads cuja
/// etapa atual da Kommo é o status_id X).
///
/// É per-unidade porque cada conta Kommo tem IDs de etapa/campo próprios — o mesmo KPI
/// pode apontar para status_ids diferentes em cada unidade.
///
/// Quando não existe linha para um (UnitId, KpiKey), o dashboard mantém o cálculo
/// padrão hardcoded (fallback) — a configuração é opt-in.
/// </summary>
public class KpiConfiguration
{
    public int Id { get; set; }

    /// <summary>Unidade (clínica) dona desta configuração.</summary>
    public int UnitId { get; set; }
    public Unit? Unit { get; set; }

    /// <summary>Tenant (Unit.ClinicId) — redundante com a unidade, mas evita join na leitura.</summary>
    public int ClinicId { get; set; }

    /// <summary>
    /// Chave do KPI no dashboard. Ex.: "total_leads", "cadastro", "resgate",
    /// "agendados", "consultas", "tratamentos", "no_show". Ver <see cref="Service.KpiKeys"/>.
    /// </summary>
    public string KpiKey { get; set; } = null!;

    /// <summary>
    /// Como o número é gerado. Valores em <see cref="Service.KpiSourceTypes"/>:
    /// <c>kommo_stage</c> | <c>custom_field_count</c> | <c>custom_field_sum</c> | <c>stage_field_filter</c>.
    /// </summary>
    public string SourceType { get; set; } = "kommo_stage";

    /// <summary>
    /// Parâmetros da fonte, em JSON. O shape varia por <see cref="SourceType"/>:
    /// <list type="bullet">
    /// <item><c>kommo_stage</c>: <c>{"pipelineId":123,"stageIds":[104945887,105018259]}</c></item>
    /// <item><c>custom_field_count</c>: <c>{"fieldId":555,"fieldCode":"CITY","matchValues":["Araguaína"]}</c></item>
    /// <item><c>custom_field_sum</c>: <c>{"fieldId":777,"fieldCode":"VALOR"}</c></item>
    /// <item><c>stage_field_filter</c>: <c>{"stageIds":[104945887],"fieldId":555,"matchValues":["Sim"]}</c></item>
    /// </list>
    /// </summary>
    public string ConfigJson { get; set; } = "{}";

    /// <summary>E-mail do analista que salvou por último (auditoria leve).</summary>
    public string? UpdatedByEmail { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
