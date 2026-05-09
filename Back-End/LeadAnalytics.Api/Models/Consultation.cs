using System.ComponentModel.DataAnnotations.Schema;

namespace LeadAnalytics.Api.Models;

/// <summary>
/// Consulta = a visita agendada / realizada do lead.
///
/// Criada a partir do webhook Cloudia quando a etapa entra em
/// 04_AGENDADO_SEM_PAGAMENTO ou 05_AGENDADO_COM_PAGAMENTO.
/// Status muda conforme novas etapas chegam (06_NAO_COMPARECEU, 07_COMPARECEU).
/// </summary>
[Table("consultations")]
public class Consultation
{
    [Column("id")]
    public int Id { get; set; }

    [Column("lead_id")]
    public int LeadId { get; set; }
    public Lead? Lead { get; set; }

    [Column("tenant_id")]
    public int TenantId { get; set; }

    [Column("unit_id")]
    public int? UnitId { get; set; }
    public Unit? Unit { get; set; }

    /// <summary>Quando o lead foi agendado pra consulta (DateTime UTC).</summary>
    [Column("scheduled_at")]
    public DateTime? ScheduledAt { get; set; }

    /// <summary>Quando a consulta efetivamente aconteceu.</summary>
    [Column("attended_at")]
    public DateTime? AttendedAt { get; set; }

    /// <summary>true se compareceu, false se faltou, null se ainda agendada.</summary>
    [Column("attended")]
    public bool? Attended { get; set; }

    /// <summary>
    /// agendada | realizada | faltou | cancelada
    /// </summary>
    [Column("status")]
    public string Status { get; set; } = "agendada";

    /// <summary>true quando a etapa de origem foi 05_AGENDADO_COM_PAGAMENTO.</summary>
    [Column("paid_in_advance")]
    public bool PaidInAdvance { get; set; }

    /// <summary>Forma de pagamento da CONSULTA (Pix/Crédito/etc) — preenchido pela SDR.</summary>
    [Column("payment_method")]
    public string? PaymentMethod { get; set; }

    /// <summary>Valor cobrado pela consulta.</summary>
    [Column("consultation_value", TypeName = "numeric(12,2)")]
    public decimal? ConsultationValue { get; set; }

    [Column("notes")]
    public string? Notes { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Tratamentos sugeridos / fechados a partir desta consulta.</summary>
    public ICollection<Treatment> Treatments { get; set; } = new List<Treatment>();
}
