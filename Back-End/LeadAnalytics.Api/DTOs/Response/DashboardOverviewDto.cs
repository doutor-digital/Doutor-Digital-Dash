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
