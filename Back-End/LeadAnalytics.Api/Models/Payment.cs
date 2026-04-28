namespace LeadAnalytics.Api.Models;

public class Payment
{
    public const decimal DefaultTreatmentValue = 3800m;

    public int Id { get; set; }

    public int LeadId { get; set; }
    public Lead Lead { get; set; } = null!;

    public int TenantId { get; set; }

    public int? UnitId { get; set; }
    public Unit? Unit { get; set; }

    // Tratamento escolhido (ex.: "Clareamento", "Ortodontia", "Implante")
    public string Treatment { get; set; } = null!;

    // Duração do tratamento em meses
    public int TreatmentDurationMonths { get; set; }

    // Valor cheio do tratamento (default 3800)
    public decimal TreatmentValue { get; set; } = DefaultTreatmentValue;

    // Forma de pagamento: pix | dinheiro | debito | credito | boleto | transferencia
    public string? PaymentMethod { get; set; } = null!;

    // Entrada (sinal) pago pelo lead
    public decimal DownPayment { get; set; }

    // Número de parcelas escolhido (1 = à vista)
    public int Installments { get; set; } = 1;

    // Valor de cada parcela (calculado: (TreatmentValue - DownPayment) / Installments)
    public decimal InstallmentValue { get; set; }

    // Valor total efetivamente pago/contratado (default = TreatmentValue)
    public decimal Amount { get; set; }

    public string? Notes { get; set; }

    public DateTime PaidAt { get; set; }
    public DateTime CreatedAt { get; set; }

    public List<PaymentSplit> Splits { get; set; } = [];
}

public static class PaymentMethodConstants
{
    public const string Composite = "composite";
}
