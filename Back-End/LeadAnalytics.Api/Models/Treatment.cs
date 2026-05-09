using System.ComponentModel.DataAnnotations.Schema;

namespace LeadAnalytics.Api.Models;

/// <summary>
/// Tratamento fechado pela SDR após a consulta.
/// Workflow:
///   webhook 09_TRATAMENTO_FECHADO chega → cria Treatment status="aguardando_dados"
///   SDR preenche tipo/duracao/valor/parcelas → status="aguardando_aprovacao"
///   Gestor aprova → status="aprovado" + Payment espelho é gerado
///   Gestor rejeita → status="rejeitado" + RejectionReason
///   webhook 17_NAO_DEU_CONTINUIDADE → ClosedAsLost=true + RejectionReason
/// </summary>
[Table("treatments")]
public class Treatment
{
    [Column("id")]
    public int Id { get; set; }

    [Column("lead_id")]
    public int LeadId { get; set; }
    public Lead? Lead { get; set; }

    [Column("consultation_id")]
    public int? ConsultationId { get; set; }
    public Consultation? Consultation { get; set; }

    [Column("tenant_id")]
    public int TenantId { get; set; }

    [Column("unit_id")]
    public int? UnitId { get; set; }

    /// <summary>"Hérnia inguinal", "Hérnia umbilical" etc — string livre por ora.</summary>
    [Column("treatment_type")]
    public string? TreatmentType { get; set; }

    [Column("duration_months")]
    public int? DurationMonths { get; set; }

    /// <summary>Valor cheio do tratamento (proposta).</summary>
    [Column("total_value", TypeName = "numeric(12,2)")]
    public decimal? TotalValue { get; set; }

    /// <summary>
    /// aguardando_dados | aguardando_aprovacao | aprovado | rejeitado | cancelado
    /// </summary>
    [Column("status")]
    public string Status { get; set; } = "aguardando_dados";

    /// <summary>Quando a SDR submeteu pra aprovação.</summary>
    [Column("submitted_at")]
    public DateTime? SubmittedAt { get; set; }

    [Column("submitted_by_user_id")]
    public int? SubmittedByUserId { get; set; }

    /// <summary>Quando o gestor aprovou ou rejeitou.</summary>
    [Column("decided_at")]
    public DateTime? DecidedAt { get; set; }

    [Column("decided_by_user_id")]
    public int? DecidedByUserId { get; set; }

    /// <summary>Vem de motivo dropdown (ainda não temos lookup) — string por ora.</summary>
    [Column("rejection_reason")]
    public string? RejectionReason { get; set; }

    /// <summary>true quando webhook 17_NAO_DEU_CONTINUIDADE bateu — distinto de "rejeitado pelo gestor".</summary>
    [Column("closed_as_lost")]
    public bool ClosedAsLost { get; set; }

    [Column("notes")]
    public string? Notes { get; set; }

    /// <summary>Espelho consolidado em Payment (preenchido após aprovação).</summary>
    [Column("payment_id")]
    public int? PaymentId { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<TreatmentInstallment> Installments { get; set; } = new List<TreatmentInstallment>();
}
