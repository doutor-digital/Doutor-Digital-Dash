namespace LeadAnalytics.Api.Service;

/// <summary>Tipos de fonte suportados por uma configuração de KPI.</summary>
public static class KpiSourceTypes
{
    /// <summary>Todos os leads criados no período (sem etapa específica). Ex.: "Total de Leads".</summary>
    public const string CreatedInPeriod = "created";

    /// <summary>Conta leads cuja etapa atual (CurrentStageId) está em stageIds.</summary>
    public const string KommoStage = "kommo_stage";

    /// <summary>Conta leads cujo campo customizado bate com algum matchValues.</summary>
    public const string CustomFieldCount = "custom_field_count";

    /// <summary>Soma o valor numérico de um campo customizado entre os leads.</summary>
    public const string CustomFieldSum = "custom_field_sum";

    /// <summary>Conta leads que estão na etapa X E têm o campo Y = Z (filtro combinado).</summary>
    public const string StageFieldFilter = "stage_field_filter";

    /// <summary>
    /// Conta leads DISTINTOS que tiveram tentativa de resgate no período, pela data do
    /// EVENTO na Kommo (preenchimento do campo "Tentativas de resgastes") — não pela
    /// data de criação do lead. Resgate é lead velho reativado; contar por criação perdia
    /// a maioria. Lê <c>recovery_attempts</c> com EntrySource="events_api".
    /// </summary>
    public const string RecoveryAttempt = "recovery_attempt";

    /// <summary>
    /// Puxa o número do CRM da FRANQUIA (Doutor Hérnia), não do Kommo. O Kommo é dono do
    /// comercial; comparecimento/falta/tratamento são do sistema clínico. Config:
    /// {"metric":"no_show"|"consultas"|"tratamentos"}.
    ///
    /// no_show/consultas vêm da API Spine (/avaliacoes). `tratamentos` vem da rota oficial
    /// /api/treatments/search — liberada em ago/2026 — e conta os LANÇADOS no período
    /// selecionado, o mesmo recorte da tela da franquia. O scrape do CRM web ficou como
    /// reserva para unidade sem token.
    /// </summary>
    public const string Franquia = "franquia";

    public static readonly string[] All =
        { CreatedInPeriod, KommoStage, CustomFieldCount, CustomFieldSum, StageFieldFilter, RecoveryAttempt, Franquia };

    public static bool IsValid(string? type) =>
        type is not null && Array.Exists(All, t => t == type);
}

/// <summary>Notas padronizadas devolvidas por <see cref="KpiConfigService.ComputeAsync"/>.</summary>
public static class KpiNotes
{
    /// <summary>
    /// A unidade não tem autorização da franquia (sem token da API Spine ou sem credencial
    /// do CRM web), então o KPI de fonte <see cref="KpiSourceTypes.Franquia"/> não tem número.
    /// É devolvido como NOTA estável (não texto livre) para o dashboard exibir
    /// "Sem autorização da franquia" no lugar de um zero — zero mentiria.
    /// </summary>
    public const string SemAutorizacaoFranquia = "sem_autorizacao_franquia";
}

/// <summary>Um KPI do dashboard que pode ser mapeado nas Configurações Técnicas.</summary>
public record KpiCatalogItem(string Key, string Label, string Description);

/// <summary>Item de upsert para <see cref="KpiConfigService.SaveAsync"/>.</summary>
public record KpiSaveItem(
    string KpiKey,
    string SourceType,
    string ConfigJson,
    bool IsCustom = false,
    string? DisplayName = null,
    string? AccentColor = null,
    string DisplayType = "number",
    int SortOrder = 0);

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
