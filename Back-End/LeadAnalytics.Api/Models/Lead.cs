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

    /// <summary>Valor do negócio (campo <c>price</c> do lead na Kommo), em R$.</summary>
    public decimal? Price { get; set; }

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

    // ─── DADOS DA KOMMO (sincronizados via REST + webhook) ────
    /// <summary>
    /// Array JSON dos custom fields do lead na Kommo:
    /// <c>[{"field_id":123,"field_name":"Cidade","field_code":"CITY","type":"text","value":"São Paulo"}, …]</c>.
    /// </summary>
    public string? CustomFieldsJson { get; set; }

    /// <summary>Array JSON das tags do lead na Kommo: <c>["VIP","Recorrente"]</c>.</summary>
    public string? TagsJson { get; set; }

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

    // ─── REVISÃO COMERCIAL (formulário /leads/:id/revisar) ────
    // "cadastro" | "resgate"
    public string? LeadType { get; set; }
    // "mensagem" | "ligacao" | "disparo_massa" (quando LeadType="resgate")
    public string? RescueType { get; set; }

    public bool? HadInteraction { get; set; }
    public bool? ScheduledConsultation { get; set; }

    public DateTime? AppointmentScheduledAt { get; set; }

    /// <summary>
    /// Quando o campo AppointmentScheduledAt foi PREENCHIDO/atualizado (última mudança
    /// do valor). Usado no card Consultas pra medir produtividade da SDR — quantos
    /// agendamentos ela marcou no dia, independente de quando a consulta vai ser.
    /// Setado no KommoIngestionService quando o valor do custom field muda, e no
    /// PUT manual do perfil quando a Revisão Comercial atualiza.
    /// </summary>
    public DateTime? AppointmentScheduledAtFilledAt { get; set; }

    // enum (sem_interacao|sem_continuidade|plano_saude|terceiros|sem_condicoes|
    //       vai_se_organizar|busca_laudo|interesse_pilates|interesse_liberacao|
    //       mora_outra_cidade|sem_interesse|clicou_engano|outro_tratamento|
    //       outra_patologia|em_viagem)
    public string? NoAppointmentReason { get; set; }
    // texto livre quando reason = "mora_outra_cidade"
    public string? NoAppointmentCity { get; set; }

    // semáforo: fechou_total|fechou_parcial|assinou_sem_entrada|decide_familia|
    //          verifica_pagamento|exame_imagem|mora_fora|outra_patologia|sem_condicoes
    public string? NoCloseReason { get; set; }

    // Consulta comparecida
    public decimal? ConsultationValue { get; set; }
    public bool? ClosedTreatment { get; set; }
    public string? IndicatedTreatment { get; set; }
    public decimal? TreatmentBudget { get; set; }

    // Tratamento fechado
    public string? TreatmentPlanCategory { get; set; }   // tratamento_pontual|clinico_mensal|...
    public decimal? TreatmentPlanValue { get; set; }

    public ICollection<LeadPaymentReceipt> PaymentReceipts { get; set; } = [];

    // ─── AUDITORIA ───────────────────────────
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? ConvertedAt { get; set; }
    public DateTime LastUpdatedAt { get; internal set; }

    /// <summary>
    /// Data REAL de criação do lead (vem do custom field "Data de criação lead" na Kommo).
    /// Usado nas agregações por dia/mês quando preenchida — fallback pra <see cref="CreatedAt"/>.
    /// Preenchida via backfill da Cloudia/CSV ou pelo webhook quando o field estiver setado.
    /// </summary>
    public DateTime? OriginalCreatedAt { get; set; }
}