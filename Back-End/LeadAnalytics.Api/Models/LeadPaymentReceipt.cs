using System.ComponentModel.DataAnnotations.Schema;

namespace LeadAnalytics.Api.Models;

[Table("lead_payment_receipts")]
public class LeadPaymentReceipt
{
    [Column("id")]
    public int Id { get; set; }

    [Column("lead_id")]
    public int LeadId { get; set; }
    public Lead? Lead { get; set; }

    [Column("tenant_id")]
    public int TenantId { get; set; }

    // "consulta" (até 2 slots) | "tratamento" (até 6 slots)
    [Column("kind")]
    public string Kind { get; set; } = "consulta";

    [Column("slot")]
    public int Slot { get; set; }

    [Column("amount")]
    public decimal? Amount { get; set; }

    // pix | dinheiro | cartao_credito | cartao_debito | boleto | transferencia | outro
    [Column("method")]
    public string? Method { get; set; }

    [Column("received_at")]
    public DateTime? ReceivedAt { get; set; }

    // se foi pagamento antecipado (linka com financeiro)
    [Column("is_advance")]
    public bool IsAdvance { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
