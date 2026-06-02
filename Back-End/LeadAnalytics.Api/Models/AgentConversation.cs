namespace LeadAnalytics.Api.Models;

/// <summary>
/// Uma conversa conduzida pela I.A. (agente-Dt) com um cliente, recebida via
/// <c>POST /webhooks/agent/{slug}</c>. Isolada por tenant (<see cref="TenantId"/>,
/// derivado da unidade do slug). Identificada de forma estável por
/// (<see cref="TenantId"/>, <see cref="ExternalId"/>) — o agente manda a conversa
/// completa e nós fazemos upsert por esse par.
///
/// Quando dá pra casar o telefone, vinculamos ao <see cref="Contact"/> e ao
/// <see cref="Lead"/> existentes pra o dashboard ligar a conversa à origem/venda.
/// </summary>
public class AgentConversation
{
    public int Id { get; set; }

    public int TenantId { get; set; }

    public int? UnitId { get; set; }
    public Unit? Unit { get; set; }

    public int? LeadId { get; set; }
    public Lead? Lead { get; set; }

    public int? ContactId { get; set; }
    public Contact? Contact { get; set; }

    /// <summary>Id estável da conversa no agente (ex.: "wa-5563999998888"). Único por tenant.</summary>
    public string ExternalId { get; set; } = null!;

    /// <summary>Nome do agente de I.A. (ex.: "agente-Dt").</summary>
    public string? AgentName { get; set; }

    /// <summary>Canal da conversa (ex.: "whatsapp", "instagram").</summary>
    public string? Channel { get; set; }

    public string? ContactName { get; set; }
    public string? ContactPhone { get; set; }
    /// <summary>Telefone só com dígitos — usado pra casar com Contact/Lead.</summary>
    public string? PhoneNormalized { get; set; }

    /// <summary>active | closed | handoff.</summary>
    public string Status { get; set; } = "active";

    /// <summary>Conversa foi transferida da I.A. para um humano?</summary>
    public bool HandedOff { get; set; }
    public DateTime? HandoffAt { get; set; }

    public string? Intent { get; set; }
    public string? Sentiment { get; set; }
    public string? Summary { get; set; }

    public int MessageCount { get; set; }

    public int? TokensIn { get; set; }
    public int? TokensOut { get; set; }

    public DateTime StartedAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public DateTime? FirstMessageAt { get; set; }
    public DateTime? LastMessageAt { get; set; }

    /// <summary>Qualquer metadado extra enviado pelo agente (JSONB).</summary>
    public string? MetadataJson { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public ICollection<AgentMessage> Messages { get; set; } = [];
}
