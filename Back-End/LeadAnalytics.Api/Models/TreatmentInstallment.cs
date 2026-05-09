using System.ComponentModel.DataAnnotations.Schema;

namespace LeadAnalytics.Api.Models;

/// <summary>
/// Parcela individual do tratamento. Sem limite de quantidade (ao contrário
/// da planilha legada que só comportava 4). Cada parcela tem forma e data
/// próprias — permite "metade Pix entrada + 12x cartão", por exemplo.
/// </summary>
[Table("treatment_installments")]
public class TreatmentInstallment
{
    [Column("id")]
    public int Id { get; set; }

    [Column("treatment_id")]
    public int TreatmentId { get; set; }
    public Treatment? Treatment { get; set; }

    /// <summary>1, 2, 3... — ordem de pagamento.</summary>
    [Column("sequence")]
    public int Sequence { get; set; }

    [Column("amount", TypeName = "numeric(12,2)")]
    public decimal Amount { get; set; }

    /// <summary>pix | dinheiro | debito | credito | boleto | transferencia.</summary>
    [Column("payment_method")]
    public string PaymentMethod { get; set; } = "pix";

    /// <summary>Data prevista/efetivada do pagamento.</summary>
    [Column("due_date")]
    public DateOnly? DueDate { get; set; }

    [Column("paid_at")]
    public DateTime? PaidAt { get; set; }

    /// <summary>pago | pendente | cancelado.</summary>
    [Column("status")]
    public string Status { get; set; } = "pendente";

    [Column("notes")]
    public string? Notes { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
