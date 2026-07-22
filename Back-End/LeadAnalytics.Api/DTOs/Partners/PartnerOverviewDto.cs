namespace LeadAnalytics.Api.DTOs.Partners;

/// <summary>
/// Uma unidade parceira no Painel de Parceiros: o cadastro (logo, cidade, responsável),
/// o estado da integração com a Kommo e os números consolidados do parceiro.
///
/// Diferente do <see cref="Units.UnitDto"/> (que é o CRUD da unidade), este DTO existe
/// para comparar parceiros lado a lado — por isso carrega as métricas agregadas.
/// </summary>
public class PartnerOverviewDto
{
    // ─── Cadastro ────────────────────────────────────────────
    public int Id { get; set; }
    public int ClinicId { get; set; }
    public string Name { get; set; } = null!;
    public string? Slug { get; set; }

    /// <summary>"saude" (clínicas) ou "juridico" (advocacia) — muda o conjunto de KPIs da unidade.</summary>
    public string Segment { get; set; } = "saude";

    public string? City { get; set; }
    public string? State { get; set; }
    public string? PhotoUrl { get; set; }
    public string? ResponsibleName { get; set; }
    public bool IsActive { get; set; }

    // ─── Integração Kommo ────────────────────────────────────
    public string? KommoSubdomain { get; set; }

    /// <summary>True quando há access token salvo — ou seja, a unidade sincroniza via API da Kommo.</summary>
    public bool HasKommoToken { get; set; }

    /// <summary>True quando o mapa de etapas da Kommo já foi configurado.</summary>
    public bool HasStageMap { get; set; }

    // ─── Números do parceiro ─────────────────────────────────
    public int TotalLeads { get; set; }
    public int Leads30d { get; set; }
    public int Leads7d { get; set; }

    /// <summary>Leads parados nas etapas de agendamento (com ou sem pagamento).</summary>
    public int Agendados { get; set; }

    /// <summary>Leads que fecharam ou já estão em tratamento.</summary>
    public int Fechados { get; set; }

    /// <summary>Soma do valor dos leads fechados/em tratamento.</summary>
    public decimal Faturamento { get; set; }

    /// <summary>Quando entrou o último lead — usado para sinalizar parceiro sem movimento.</summary>
    public DateTime? LastLeadAt { get; set; }

    /// <summary>Dias desde o último lead. Null quando o parceiro nunca recebeu lead.</summary>
    public int? DaysSinceLastLead { get; set; }
}
