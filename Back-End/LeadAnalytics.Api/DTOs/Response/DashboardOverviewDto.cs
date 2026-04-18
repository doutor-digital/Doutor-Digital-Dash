using System.Text.Json.Serialization;
using LeadAnalytics.Api.DTOs.Cloudia;

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

    // ─── Estados da conversa (bot/queue/service/concluido) ─────────
    public LeadsCountDto States { get; set; } = new();

    // ─── Distribuições ─────────────────────────────────────────────
    public List<EtapaAgrupadaDto> Etapas { get; set; } = new();
    public List<OrigemAgrupadaDto> Origens { get; set; } = new();
}
