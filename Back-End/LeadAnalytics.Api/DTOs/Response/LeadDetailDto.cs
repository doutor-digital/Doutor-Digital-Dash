namespace LeadAnalytics.Api.DTOs.Response;

public class LeadDetailDto
{
    public int Id { get; set; }
    public int ExternalId { get; set; }
    public int TenantId { get; set; }

    public string Name { get; set; } = null!;
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Cpf { get; set; }
    public string? Gender { get; set; }

    public string Source { get; set; } = null!;
    public string Channel { get; set; } = null!;
    public string Campaign { get; set; } = null!;
    public string? Ad { get; set; }
    public string TrackingConfidence { get; set; } = null!;

    public string CurrentStage { get; set; } = null!;
    public int? CurrentStageId { get; set; }
    public string Status { get; set; } = null!;
    public string? ConversationState { get; set; }

    public bool HasAppointment { get; set; }
    public bool HasPayment { get; set; }
    public bool? HasHealthInsurancePlan { get; set; }
    public string? Observations { get; set; }
    public List<string> Tags { get; set; } = new();

    // "compareceu" | "faltou" | "aguardando" | null
    public string? AttendanceStatus { get; set; }
    public DateTime? AttendanceStatusAt { get; set; }

    public int? UnitId { get; set; }
    public string? UnitName { get; set; }

    public int? AttendantId { get; set; }
    public string? AttendantName { get; set; }
    public string? AttendantEmail { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? ConvertedAt { get; set; }

    /// <summary>Quente/Morno/Frio — vinha preenchido no banco e não era devolvido.</summary>
    public string? Qualification { get; set; }
    public DateTime? QualificationFilledAt { get; set; }

    /// <summary>Quando a SDR preencheu a data da consulta (diferente da data em si).</summary>
    public DateTime? AppointmentScheduledAtFilledAt { get; set; }

    /// <summary>Valor do lead na Kommo.</summary>
    public decimal? Price { get; set; }

    /// <summary>Data de criação original, quando a nossa foi a do primeiro sync.</summary>
    public DateTime? OriginalCreatedAt { get; set; }

    /// <summary>Todos os campos preenchidos do cartão da Kommo, com nome legível.</summary>
    public List<LeadCustomFieldDto> CamposKommo { get; set; } = [];

    public List<LeadStageHistoryDto> StageHistory { get; set; } = new();
    public List<LeadConversationDto> Conversations { get; set; } = new();
    public List<LeadAssignmentDto> Assignments { get; set; } = new();
    public List<LeadPaymentDto> Payments { get; set; } = new();

    // ─── Revisão comercial (formulário /leads/:id/revisar) ───
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

    public List<LeadPaymentReceiptDto> PaymentReceipts { get; set; } = new();
}

public class LeadPaymentReceiptDto
{
    public int Id { get; set; }
    public string Kind { get; set; } = "consulta";   // consulta | tratamento
    public int Slot { get; set; }
    public decimal? Amount { get; set; }
    public string? Method { get; set; }
    public DateTime? ReceivedAt { get; set; }
    public bool IsAdvance { get; set; }
}

public class LeadStageHistoryDto
{
    public int Id { get; set; }
    public int StageId { get; set; }
    public string StageLabel { get; set; } = null!;
    public DateTime ChangedAt { get; set; }

    /// <summary>webhook · events_api · legacy</summary>
    public string? EntrySource { get; set; }

    /// <summary>
    /// A data é o instante real da mudança de etapa. Falso nas linhas legadas, que guardam a
    /// data do sync — mostrá-las como hora da transição é o erro que faz a tela dizer que o
    /// lead mudou de etapa às 3h da manhã.
    /// </summary>
    public bool DataConfiavel { get; set; }
}

/// <summary>
/// Um campo do cartão da Kommo, já com nome e valor legíveis.
///
/// É onde mora metade da ficha do lead — origem, motivo, qualificação, tipo, valores, e o
/// "Pausar IA". Ficava tudo guardado em CustomFieldsJson e nunca chegava na tela: o detalhe do
/// lead mostrava dez colunas e escondia trinta e três campos.
/// </summary>
public class LeadCustomFieldDto
{
    public long FieldId { get; set; }
    public string Nome { get; set; } = string.Empty;
    public string Valor { get; set; } = string.Empty;

    /// <summary>
    /// O valor era um carimbo unix e foi convertido para data. A Kommo devolve data como
    /// número, e "1757539204" na tela não é informação.
    /// </summary>
    public bool EhData { get; set; }

    /// <summary>
    /// Falso quando o campo existe no cartão e está em branco. Vem junto de propósito: o que
    /// a SDR NÃO preencheu é metade do diagnóstico, e some se só devolvermos o preenchido.
    /// </summary>
    public bool Preenchido { get; set; }
}

public class LeadConversationDto
{
    public int Id { get; set; }
    public string Channel { get; set; } = null!;
    public string? Source { get; set; }
    public string ConversationState { get; set; } = null!;
    public DateTime StartedAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public int? AttendantId { get; set; }
    public string? AttendantName { get; set; }
    public List<LeadInteractionDto> Interactions { get; set; } = new();
}

public class LeadInteractionDto
{
    public int Id { get; set; }
    public string Type { get; set; } = null!;
    public string? Content { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class LeadAssignmentDto
{
    public int Id { get; set; }
    public int AttendantId { get; set; }
    public string? AttendantName { get; set; }
    public string? Stage { get; set; }
    public DateTime AssignedAt { get; set; }
}

public class LeadPaymentDto
{
    public int Id { get; set; }
    public decimal Amount { get; set; }
    public DateTime PaidAt { get; set; }
}
