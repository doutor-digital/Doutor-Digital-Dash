namespace LeadAnalytics.Api.Models;

public class Lead
{
    // ─── IDENTIDADE ───────────────────────────
    public int Id { get; set; }
    public int ExternalId { get; set; }
    public int TenantId { get; set; }

    // ─── DADOS BÁSICOS ────────────────────────
    public string Name { get; set; } = null!;
    public string Phone { get; set; } = null!;
    public string? Email { get; set; }
    public string? Cpf { get; set; }
    public string? Gender { get; set; }

    // ─── ATRIBUIÇÃO (PROCESSADO) ─────────────
    public string Source { get; set; } = "DESCONHECIDO";   // Facebook, Instagram
    public string Channel { get; set; } = "DESCONHECIDO";  // WhatsApp, Direct
    public string Campaign { get; set; } = "DESCONHECIDO"; // DISPARO, ORGANICO
    public string? Ad { get; set; }

    public string TrackingConfidence { get; set; } = "BAIXA";

    // ─── ESTADO DO NEGÓCIO ───────────────────
    public string CurrentStage { get; set; } = "NOVO";
    public int? CurrentStageId { get; set; }

    public string Status { get; set; } = "new";

    public bool HasAppointment { get; set; }
    public bool HasPayment { get; set; }

    // "compareceu" | "faltou" | "aguardando" | null (ação manual na tela de contatos)
    public string? AttendanceStatus { get; set; }
    public DateTime? AttendanceStatusAt { get; set; }

    // ─── CONTEXTO ────────────────────────────
    public string? ConversationState { get; set; } // cache opcional
    public string? Observations { get; set; }

    public bool? HasHealthInsurancePlan { get; set; }

    // ─── INTEGRAÇÃO ──────────────────────────
    public string? IdFacebookApp { get; set; }
    public int? IdChannelIntegration { get; set; }
    public string? LastAdId { get; set; }

    // ─── RAW DATA (NÃO CONFIÁVEL) ────────────
    public string? Tags { get; set; }

    // ─── RELACIONAMENTOS ─────────────────────
    public int? UnitId { get; set; }
    public Unit? Unit { get; set; }

    public ICollection<LeadStageHistory> StageHistory { get; set; } = [];
    public ICollection<LeadConversation> Conversations { get; set; } = [];
    public ICollection<Payment> Payments { get; set; } = [];

    public int? AttendantId { get; set; }
    public Attendant? Attendant { get; set; }

    // Histórico de atribuições
    public List<LeadAssignment> Assignments { get; set; } = new();

    // ─── AUDITORIA ───────────────────────────
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? ConvertedAt { get; set; }
    public DateTime LastUpdatedAt { get; internal set; }
}