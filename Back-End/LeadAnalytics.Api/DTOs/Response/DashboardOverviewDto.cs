using System.Text.Json.Serialization;

namespace LeadAnalytics.Api.DTOs.Response;

public class DashboardOverviewDto
{
    [JsonPropertyName("date_from")]
    public DateTime DateFrom { get; set; }

    [JsonPropertyName("date_to")]
    public DateTime DateTo { get; set; }

    // ─── KPIs principais ───────────────────────────────────────────
    [JsonPropertyName("total_leads")]
    public int TotalLeads { get; set; }

    /// <summary>Leads deletados na Kommo dentro do período (Status="deleted").</summary>
    [JsonPropertyName("total_leads_deleted")]
    public int TotalLeadsDeleted { get; set; }

    public int Consultas { get; set; }

    [JsonPropertyName("com_pagamento")]
    public int ComPagamento { get; set; }

    [JsonPropertyName("sem_pagamento")]
    public int SemPagamento { get; set; }

    [JsonPropertyName("conversao_rate")]
    public double ConversaoRate { get; set; }

    [JsonPropertyName("pagamento_rate")]
    public double PagamentoRate { get; set; }

    [JsonPropertyName("sem_pagamento_rate")]
    public double SemPagamentoRate { get; set; }

    // ─── Comparecimento / fechamento ───────────────────────────────
    [JsonPropertyName("consultas_agendadas")]
    public int ConsultasAgendadas { get; set; }

    public int Compareceu { get; set; }

    public int Faltou { get; set; }

    [JsonPropertyName("nao_fechou")]
    public int NaoFechou { get; set; }

    public int Fechou { get; set; }

    [JsonPropertyName("leads_ativos")]
    public int LeadsAtivos { get; set; }

    [JsonPropertyName("comparecimento_rate")]
    public double ComparecimentoRate { get; set; }

    [JsonPropertyName("fechamento_rate")]
    public double FechamentoRate { get; set; }

    // ─── Estados da conversa (bot/queue/service/concluido) ─────────
    public LeadsCountDto States { get; set; } = new();

    /// <summary>
    /// Valores de KPI vindos das Configurações Técnicas (mapeamento por unidade).
    /// Quando uma chave existe aqui (ex.: "resgate"), o front prefere esse número
    /// ao cálculo padrão. Só é preenchido quando uma unidade específica é selecionada.
    /// </summary>
    [JsonPropertyName("kpi_overrides")]
    public Dictionary<string, double> KpiOverrides { get; set; } = new();

    /// <summary>
    /// KPIs criados pelo analista do zero (nome + cor + fonte próprios), já com o valor
    /// calculado para o período. O front renderiza um card por item, abaixo dos fixos.
    /// Só preenchido quando uma unidade específica é selecionada.
    /// </summary>
    [JsonPropertyName("custom_kpis")]
    public List<CustomKpiDto> CustomKpis { get; set; } = new();

    // ─── Distribuições ─────────────────────────────────────────────
    public List<EtapaAgrupadaDto> Etapas { get; set; } = new();
    public List<OrigemAgrupadaDto> Origens { get; set; } = new();

    /// <summary>Origens dos leads que chegaram a alguma consulta (agendados+).</summary>
    [JsonPropertyName("origens_consultas")]
    public List<OrigemAgrupadaDto> OrigensConsultas { get; set; } = new();

    /// <summary>Origens dos leads que fecharam tratamento.</summary>
    [JsonPropertyName("origens_tratamentos")]
    public List<OrigemAgrupadaDto> OrigensTratamentos { get; set; } = new();

    // ─── Funnel por tipo de base ───────────────────────────────────
    /// <summary>Funnel completo (todos os leads do período).</summary>
    [JsonPropertyName("funnel_leads")]
    public FunnelGroupDto FunnelLeads { get; set; } = new();

    /// <summary>Funnel só dos leads com LeadType = "cadastro".</summary>
    [JsonPropertyName("funnel_cadastro")]
    public FunnelGroupDto FunnelCadastro { get; set; } = new();

    /// <summary>Funnel só dos leads com LeadType = "resgate".</summary>
    [JsonPropertyName("funnel_resgate")]
    public FunnelGroupDto FunnelResgate { get; set; } = new();

    // ─── Séries temporais ──────────────────────────────────────────
    /// <summary>Leads agrupados por semana ISO (yyyy-Www).</summary>
    [JsonPropertyName("leads_por_semana")]
    public List<PeriodoQtdDto> LeadsPorSemana { get; set; } = new();

    /// <summary>Consultas (CompareceuConsulta + FechouTratamento) por semana.</summary>
    [JsonPropertyName("consultas_por_semana")]
    public List<PeriodoQtdDto> ConsultasPorSemana { get; set; } = new();

    /// <summary>Tratamentos fechados por semana.</summary>
    [JsonPropertyName("tratamentos_por_semana")]
    public List<PeriodoQtdDto> TratamentosPorSemana { get; set; } = new();

    /// <summary>Leads por dia da semana (1=Dom .. 7=Sab) — agregado ao longo do período.</summary>
    [JsonPropertyName("leads_por_dia_semana")]
    public List<DiaSemanaQtdDto> LeadsPorDiaSemana { get; set; } = new();
}

/// <summary>Um KPI custom já resolvido para o dashboard (definição + valor do período).</summary>
public class CustomKpiDto
{
    /// <summary>Chave gerada (ex.: "custom_ab12…") — usada no drill-down por kpi_key.</summary>
    public string Key { get; set; } = null!;

    /// <summary>Nome exibido no card.</summary>
    public string Label { get; set; } = "";

    /// <summary>Cor da borda superior (hex).</summary>
    public string? Color { get; set; }

    /// <summary>Valor calculado no período (para gráfico = soma do breakdown).</summary>
    public double Value { get; set; }

    [JsonPropertyName("source_type")]
    public string SourceType { get; set; } = "";

    /// <summary><c>number</c> (um número) ou <c>source_chart</c> (gráfico de origens).</summary>
    [JsonPropertyName("display_type")]
    public string DisplayType { get; set; } = "number";

    /// <summary>Distribuição por valor — preenchido só quando display_type = source_chart.</summary>
    public List<KpiBreakdownItemDto> Breakdown { get; set; } = new();

    [JsonPropertyName("sort_order")]
    public int SortOrder { get; set; }
}

/// <summary>Uma fatia do gráfico de um KPI custom (ex.: "Instagram" → 42).</summary>
public class KpiBreakdownItemDto
{
    public string Label { get; set; } = "";
    public int Value { get; set; }
}

public class PeriodoQtdDto
{
    public string Periodo { get; set; } = string.Empty;
    public int Quantidade { get; set; }
}

public class DiaSemanaQtdDto
{
    /// <summary>1=Dom, 2=Seg, 3=Ter, 4=Qua, 5=Qui, 6=Sex, 7=Sáb.</summary>
    public int Dia { get; set; }
    public int Quantidade { get; set; }
}
