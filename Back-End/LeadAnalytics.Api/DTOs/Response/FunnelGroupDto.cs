using System.Text.Json.Serialization;

namespace LeadAnalytics.Api.DTOs.Response;

/// <summary>
/// Desdobramento do funil para um subgrupo de leads (todos / cadastro / resgate).
/// As taxas (% conversão entre etapas) são calculadas no front com base nestes contadores.
/// </summary>
public class FunnelGroupDto
{
    public int Total { get; set; }

    /// <summary>Leads onde HadInteraction = true (alguém respondeu/atendeu).</summary>
    public int Interacoes { get; set; }

    /// <summary>Leads em AGENDADO_COM_PAGAMENTO ou AGENDADO_SEM_PAGAMENTO.</summary>
    public int Agendados { get; set; }

    /// <summary>Leads que vieram a uma consulta (EmTratamento ou FechouTratamento).</summary>
    public int Consultas { get; set; }

    /// <summary>Leads em FechouTratamento (tratamento confirmado/iniciado).</summary>
    public int Tratamentos { get; set; }

    /// <summary>No-show: agendados que faltaram (07_FALTOU) — KPI de primeira linha.</summary>
    [JsonPropertyName("no_show")]
    public int NoShow { get; set; }
}
