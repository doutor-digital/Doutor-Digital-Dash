namespace LeadAnalytics.Api.Service;

/// <summary>Tipos de fonte suportados por uma configuração de KPI.</summary>
public static class KpiSourceTypes
{
    /// <summary>Conta leads cuja etapa atual (CurrentStageId) está em stageIds.</summary>
    public const string KommoStage = "kommo_stage";

    /// <summary>Conta leads cujo campo customizado bate com algum matchValues.</summary>
    public const string CustomFieldCount = "custom_field_count";

    /// <summary>Soma o valor numérico de um campo customizado entre os leads.</summary>
    public const string CustomFieldSum = "custom_field_sum";

    /// <summary>Conta leads que estão na etapa X E têm o campo Y = Z (filtro combinado).</summary>
    public const string StageFieldFilter = "stage_field_filter";

    public static readonly string[] All =
        { KommoStage, CustomFieldCount, CustomFieldSum, StageFieldFilter };

    public static bool IsValid(string? type) =>
        type is not null && Array.Exists(All, t => t == type);
}

/// <summary>Um KPI do dashboard que pode ser mapeado nas Configurações Técnicas.</summary>
public record KpiCatalogItem(string Key, string Label, string Description);

/// <summary>
/// Catálogo dos KPIs do dashboard que o analista pode reconfigurar. A chave (Key) casa
/// com <see cref="Models.KpiConfiguration.KpiKey"/> e com os cards da DashboardPage.
/// </summary>
public static class KpiCatalog
{
    public static readonly IReadOnlyList<KpiCatalogItem> Items = new List<KpiCatalogItem>
    {
        new("total_leads", "Total de Leads",  "Volume total de leads no período."),
        new("cadastro",    "Cadastro",        "Leads do tipo cadastro."),
        new("resgate",     "Resgate",         "Leads do tipo resgate / reativação."),
        new("agendados",   "Agendados",       "Leads que chegaram a agendar consulta."),
        new("no_show",     "No-show",         "Agendados que não compareceram."),
        new("consultas",   "Consultas",       "Consultas realizadas (compareceram)."),
        new("tratamentos", "Tratamentos",     "Leads que fecharam tratamento."),
        new("interacoes",  "Interações",      "Leads que tiveram alguma interação."),
    };

    public static bool IsValidKey(string? key) =>
        key is not null && Items.Any(i => i.Key == key);
}
