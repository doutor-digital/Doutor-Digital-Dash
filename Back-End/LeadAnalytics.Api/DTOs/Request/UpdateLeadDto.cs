namespace LeadAnalytics.Api.DTOs.Request;

/// <summary>
/// Payload do PATCH /webhooks/{id}. Todos os campos são opcionais —
/// só os enviados são aplicados (sentinel = null não muda).
/// Para limpar um campo, envie string vazia ou explícito null via convention
/// (o front-end usa `null` para limpar e omite chave para "não tocar").
/// </summary>
public class UpdateLeadDto
{
    // ─── Identificação ──────────────────────────────
    public string? Name { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Cpf { get; set; }
    public string? Gender { get; set; }

    // ─── Atribuição ────────────────────────────────
    public string? Source { get; set; }
    public string? Channel { get; set; }
    public string? Campaign { get; set; }
    public string? Ad { get; set; }

    // ─── Estado ────────────────────────────────────
    public string? CurrentStage { get; set; }
    public bool? HasAppointment { get; set; }
    public bool? HasPayment { get; set; }
    public bool? HasHealthInsurancePlan { get; set; }
    public string? Observations { get; set; }
    public List<string>? Tags { get; set; }

    // ─── Atribuição ──────────────────────────────
    public int? UnitId { get; set; }
    public int? AttendantId { get; set; }

    // ─── Revisão comercial ──────────────────────
    public string? LeadType { get; set; }
    public string? RescueType { get; set; }
    public bool? HadInteraction { get; set; }
    public bool? ScheduledConsultation { get; set; }
    public DateTime? AppointmentScheduledAt { get; set; }
    public string? NoAppointmentReason { get; set; }
    public string? NoAppointmentCity { get; set; }
    public string? NoCloseReason { get; set; }
    public decimal? ConsultationValue { get; set; }
    public bool? ClosedTreatment { get; set; }
    public string? IndicatedTreatment { get; set; }
    public decimal? TreatmentBudget { get; set; }
    public string? TreatmentPlanCategory { get; set; }
    public decimal? TreatmentPlanValue { get; set; }

    // Substituição completa (replace) — se enviado, aplica como verdade
    public List<PaymentReceiptInput>? PaymentReceipts { get; set; }
}

public class PaymentReceiptInput
{
    public string Kind { get; set; } = "consulta";   // consulta | tratamento
    public int Slot { get; set; }
    public decimal? Amount { get; set; }
    public string? Method { get; set; }
    public DateTime? ReceivedAt { get; set; }
    public bool IsAdvance { get; set; }
}
