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
    /// comercial; agendamento/comparecimento/falta/tratamento são do sistema clínico. Config:
    /// {"metric":"agendados"|"no_show"|"consultas"|"tratamentos"|"receita"}.
    ///
    /// agendados/no_show/consultas vêm da API Spine (/avaliacoes). `tratamentos` vem da rota
    /// oficial /api/treatments/search — liberada em ago/2026 — e conta os LANÇADOS no período
    /// selecionado, o mesmo recorte da tela da franquia. O scrape do CRM web ficou como
    /// reserva para unidade sem token.
    ///
    /// O recorte de TODAS elas é a data do FATO (o que aconteceu na clínica no período), não
    /// a data de entrada do lead. É por isso que estes números não batem com um funil por
    /// safra de lead da Kommo — são perguntas diferentes, as duas certas.
    /// </summary>
    public const string Franquia = "franquia";

    /// <summary>
    /// As únicas métricas aceitas em <c>{"metric": "..."}</c> para <see cref="Franquia"/>.
    /// Validar contra esta lista ANTES de bater na franquia é o que impede uma métrica
    /// escrita errada no seed de virar silenciosamente o número de outro card.
    /// </summary>
    public static readonly string[] MetricasFranquia =
        { "agendados", "consultas", "no_show", "tratamentos", "receita", "receita_qtd" };

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

    /// <summary>
    /// A unidade TEM autorização, mas a franquia devolveu os tratamentos sem preço —
    /// medido em 28/08/2026: em Açailândia, Balsas e Serra as linhas vêm todas com
    /// `price` nulo, enquanto nas outras sete vem preenchido.
    ///
    /// Preço ausente não é receita zero. Publicar 0 diria "não vendeu nada" numa
    /// unidade que lançou 18 tratamentos — o mesmo erro de sempre, com outra cara.
    /// </summary>
    public const string SemValorFranquia = "sem_valor_franquia";

    /// <summary>
    /// Receita de um período que o cruzamento franquia × Kommo NUNCA olhou (antes de a
    /// unidade entrar no cruzamento, ou hoje antes das 05:40). Zero vínculo aqui não quer
    /// dizer "não vendeu": quer dizer "não sabemos". O card mostra "—", nunca R$ 0.
    /// </summary>
    public const string CruzamentoNaoRodou = "cruzamento_nao_rodou";

    /// <summary>
    /// Período maior do que o teto que o painel aceita ler da agenda da franquia de uma vez
    /// (<see cref="Spine.SpineAvaliacoesService.MaxDiasAgendaKpi"/>). A agenda só responde
    /// 100 dias por consulta; o painel fatia até esse teto e, acima dele, não pede — para
    /// não despejar dezenas de chamadas na franquia por uma tela.
    /// </summary>
    public const string PeriodoLongoDemaisAgenda = "periodo_longo_demais_agenda";

    /// <summary>
    /// O texto que o card mostra embaixo do "—" quando o KPI ficou sem número. A nota
    /// técnica (código estável ou mensagem de exceção) nunca vai crua para a tela.
    /// </summary>
    public static string MotivoSemNumero(string? nota) => nota switch
    {
        SemValorFranquia =>
            "Nenhum tratamento do período tem valor — nem na franquia, nem no card da Kommo.",
        CruzamentoNaoRodou =>
            "O cruzamento ainda não rodou para este período (roda todo dia às 05:40).",
        PeriodoLongoDemaisAgenda =>
            $"Período maior que {Spine.SpineAvaliacoesService.MaxDiasAgendaKpi} dias: a agenda da franquia não é lida de uma vez. Escolha um período menor.",
        SemAutorizacaoFranquia => "Sem autorização da franquia nesta unidade.",
        _ => "A franquia não respondeu agora. Tente de novo em alguns minutos.",
    };
}

/// <summary>
/// Como a medida de um KPI entra na resposta do dashboard (<c>dashboard-overview</c>).
/// Uma regra só para a unidade e para "Todas as unidades" — antes eram dois blocos copiados
/// no controller, e os dois publicavam 0 quando a medida não existia.
/// </summary>
public static class PublicacaoDeKpi
{
    /// <summary>
    /// Valor presente → vira número (<c>kpi_overrides</c>). Valor NULO → nunca vira número:
    /// ou entra em <c>kpis_sem_autorizacao</c> (cadeado), ou em <c>kpis_sem_numero</c> com o
    /// motivo que o card escreve embaixo do "—". Sem isto o front caía no número antigo da
    /// Kommo (Agendados) ou mostrava R$ 0 (Receita).
    /// </summary>
    public static void Publicar(
        DTOs.Response.DashboardOverviewDto resultado, string chave, double? valor, string? nota)
    {
        if (valor is null)
        {
            if (nota == KpiNotes.SemAutorizacaoFranquia)
                resultado.KpisSemAutorizacao.Add(chave);
            else
                resultado.KpisSemNumero[chave] = KpiNotes.MotivoSemNumero(nota);
            return;
        }

        resultado.KpiOverrides[chave] = valor.Value;

        // Hoje só a Receita tem nota embaixo do número ("24 tratamentos · 3 sem valor").
        if (chave == "receita" && !string.IsNullOrWhiteSpace(nota))
            resultado.KpiNotas[chave] = nota;
    }
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
