using System.ComponentModel.DataAnnotations;

namespace LeadAnalytics.Api.DTOs.Sdr;

// ───────────────────────────────────────────────────────────────────────────
// Lead — request
// ───────────────────────────────────────────────────────────────────────────

public class SdrLeadCreateDto
{
    [Required, StringLength(180)] public string Nome { get; set; } = null!;
    [Required, StringLength(40)]  public string Telefone { get; set; } = null!;
    [Required, StringLength(20)]  public string Tipo { get; set; } = "Cadastro";
    [Required, StringLength(80)]  public string Origem { get; set; } = "Sem origem";
    [StringLength(60)]            public string? TipoResgate { get; set; }
    public bool Interacao { get; set; }
    public bool AgendouConsulta { get; set; }
    public DateTime? DataAgendamento { get; set; }
    [StringLength(120)]           public string? MotivoNaoAgendamento { get; set; }
    [Required, StringLength(120)] public string NomeResponsavel { get; set; } = null!;
    [StringLength(180)]           public string? Login { get; set; }
    [StringLength(2000)]          public string? Observacao { get; set; }
    [StringLength(80)]            public string? Situacao { get; set; }
    [StringLength(180)]           public string? Clinica { get; set; }

    /// <summary>"manual" | "importado" — manual entry vai direto pra "aprovado".</summary>
    [StringLength(20)]            public string Source { get; set; } = "manual";

    public int? UnitId { get; set; }
    public int? AttendantId { get; set; }
    public int? ImportBatchId { get; set; }
}

public class SdrLeadUpdateDto : SdrLeadCreateDto
{
    /// <summary>Lista de chaves do lead que vieram da Cloudia. Mantém quando o usuário edita.</summary>
    public List<string>? CloudiaFields { get; set; }
}

public class SdrLeadReviewActionDto
{
    [Required, StringLength(20)]
    public string Action { get; set; } = "approve"; // approve | reject

    [StringLength(500)]
    public string? RejectionReason { get; set; }

    /// <summary>Snapshot final dos campos depois da revisão (caso a SDR tenha editado).</summary>
    public SdrLeadUpdateDto? Patch { get; set; }
}

// ───────────────────────────────────────────────────────────────────────────
// Lead — response
// ───────────────────────────────────────────────────────────────────────────

public class SdrLeadResponseDto
{
    public int Id { get; set; }
    public int TenantId { get; set; }
    public int? ExternalId { get; set; }

    public string Nome { get; set; } = null!;
    public string Telefone { get; set; } = null!;
    public string Tipo { get; set; } = null!;
    public string Origem { get; set; } = null!;
    public string? TipoResgate { get; set; }
    public bool Interacao { get; set; }
    public bool AgendouConsulta { get; set; }
    public DateTime? DataAgendamento { get; set; }
    public string? MotivoNaoAgendamento { get; set; }
    public string NomeResponsavel { get; set; } = null!;
    public string? Login { get; set; }
    public string? Observacao { get; set; }
    public string? Situacao { get; set; }
    public string? Clinica { get; set; }

    public DateTime DataOrigem { get; set; }
    public DateTime? DataModificacao { get; set; }

    public string Source { get; set; } = null!;
    public string Status { get; set; } = null!;
    public DateTime? ReviewedAt { get; set; }
    public int? ReviewedByUserId { get; set; }
    public string? ReviewedByName { get; set; }
    public string? RejectionReason { get; set; }

    public List<string> CloudiaFields { get; set; } = [];
    public DateTime? CloudiaReceivedAt { get; set; }
    public string? CloudiaWebhookEvent { get; set; }

    public int? UnitId { get; set; }
    public int? AttendantId { get; set; }
    public int? ImportBatchId { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

// ───────────────────────────────────────────────────────────────────────────
// Consulta
// ───────────────────────────────────────────────────────────────────────────

public class SdrConsultaCreateDto
{
    public int SdrLeadId { get; set; }
    public DateTime DataConsulta { get; set; }
    public decimal ValorConsulta { get; set; }
    public bool Pago { get; set; }
    [StringLength(20)]  public string? Status { get; set; }
    [StringLength(180)] public string? TipoTratamentoIndicado { get; set; }
    public decimal? ValorTratamento { get; set; }
    public bool? FechouTratamento { get; set; }
    [StringLength(180)] public string? MotivoNaoFechamento { get; set; }
    [StringLength(2000)] public string? Observacao { get; set; }
    public List<SdrRecebimentoInputDto>? Recebimentos { get; set; }
}

public class SdrConsultaResponseDto
{
    public int Id { get; set; }
    public int SdrLeadId { get; set; }
    public DateTime DataConsulta { get; set; }
    public decimal ValorConsulta { get; set; }
    public bool Pago { get; set; }
    public string? Status { get; set; }
    public string? TipoTratamentoIndicado { get; set; }
    public decimal? ValorTratamento { get; set; }
    public bool? FechouTratamento { get; set; }
    public string? MotivoNaoFechamento { get; set; }
    public string? Observacao { get; set; }
    public List<SdrRecebimentoResponseDto> Recebimentos { get; set; } = [];
    public decimal TotalRecebido { get; set; }
    public decimal FaltaReceber { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

// ───────────────────────────────────────────────────────────────────────────
// Tratamento
// ───────────────────────────────────────────────────────────────────────────

public class SdrTratamentoCreateDto
{
    public int SdrConsultaId { get; set; }
    public decimal Valor { get; set; }
    [StringLength(20)] public string? Status { get; set; }
    [StringLength(20)] public string? Tipo { get; set; }
    [StringLength(500)] public string? Descricao { get; set; }
    [StringLength(2000)] public string? Observacao { get; set; }
    [StringLength(80)] public string? Situacao { get; set; }
    public List<SdrRecebimentoInputDto>? Recebimentos { get; set; }
}

public class SdrTratamentoResponseDto
{
    public int Id { get; set; }
    public int SdrConsultaId { get; set; }
    public int SdrLeadId { get; set; }
    public decimal Valor { get; set; }
    public string? Status { get; set; }
    public string? Tipo { get; set; }
    public string? Descricao { get; set; }
    public string? Observacao { get; set; }
    public string? Situacao { get; set; }
    public List<SdrRecebimentoResponseDto> Recebimentos { get; set; } = [];
    public decimal TotalRecebido { get; set; }
    public decimal FaltaReceber { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

// ───────────────────────────────────────────────────────────────────────────
// Recebimento
// ───────────────────────────────────────────────────────────────────────────

public class SdrRecebimentoInputDto
{
    public int Ordem { get; set; }
    public decimal Valor { get; set; }
    [Required, StringLength(40)] public string FormaPagamento { get; set; } = null!;
    public DateTime DataRecebimento { get; set; }
    [StringLength(500)] public string? Notes { get; set; }
}

public class SdrRecebimentoResponseDto
{
    public int Id { get; set; }
    public int? SdrConsultaId { get; set; }
    public int? SdrTratamentoId { get; set; }
    public int Ordem { get; set; }
    public decimal Valor { get; set; }
    public string FormaPagamento { get; set; } = null!;
    public DateTime DataRecebimento { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }
}

// ───────────────────────────────────────────────────────────────────────────
// Tarefa
// ───────────────────────────────────────────────────────────────────────────

public class SdrTarefaCreateDto
{
    [Required, StringLength(180)] public string Nome { get; set; } = null!;
    [StringLength(2000)] public string? Descricao { get; set; }
    public DateTime DataVencimento { get; set; }
    [StringLength(20)] public string Prioridade { get; set; } = "media";
    [StringLength(20)] public string Status { get; set; } = "pendente";
    [StringLength(2000)] public string? Observacao { get; set; }
    [StringLength(180)] public string? ResponsavelLogin { get; set; }
    public int? SdrLeadId { get; set; }
}

public class SdrTarefaUpdateDto : SdrTarefaCreateDto { }

public class SdrTarefaResponseDto
{
    public int Id { get; set; }
    public string Nome { get; set; } = null!;
    public string? Descricao { get; set; }
    public DateTime DataVencimento { get; set; }
    public string Prioridade { get; set; } = null!;
    public string Status { get; set; } = null!;
    public string? Observacao { get; set; }
    public string? ResponsavelLogin { get; set; }
    public int? SdrLeadId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? ConcludedAt { get; set; }
}

// ───────────────────────────────────────────────────────────────────────────
// Agenda
// ───────────────────────────────────────────────────────────────────────────

public class SdrAgendaCreateDto
{
    public DateTime Data { get; set; }
    [Required, StringLength(5)] public string HoraInicio { get; set; } = null!;
    [Required, StringLength(5)] public string HoraFim { get; set; } = null!;
    [Required, StringLength(180)] public string Nome { get; set; } = null!;
    [Required, StringLength(500)] public string Descricao { get; set; } = null!;
    [StringLength(20)] public string Status { get; set; } = "agendado";
    [StringLength(2000)] public string? Observacao { get; set; }
    [StringLength(180)] public string? ResponsavelLogin { get; set; }
    public int? SdrLeadId { get; set; }
}

public class SdrAgendaUpdateDto : SdrAgendaCreateDto { }

public class SdrAgendaResponseDto
{
    public int Id { get; set; }
    public DateTime Data { get; set; }
    public string HoraInicio { get; set; } = null!;
    public string HoraFim { get; set; } = null!;
    public string Nome { get; set; } = null!;
    public string Descricao { get; set; } = null!;
    public string Status { get; set; } = null!;
    public string? Observacao { get; set; }
    public string? ResponsavelLogin { get; set; }
    public int? SdrLeadId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

// ───────────────────────────────────────────────────────────────────────────
// Meta
// ───────────────────────────────────────────────────────────────────────────

public class SdrMetaUpsertDto
{
    [Required, StringLength(7)] public string Mes { get; set; } = null!;
    [Required, StringLength(180)] public string Unidade { get; set; } = null!;
    [Required, StringLength(180)] public string Login { get; set; } = null!;
    [Required, StringLength(120)] public string Secretaria { get; set; } = null!;
    public decimal MetaValor { get; set; }
    public int RealCadastro { get; set; }
    public int RealResgate { get; set; }
    public int QtdTotal { get; set; }
}

public class SdrMetaResponseDto
{
    public int Id { get; set; }
    public string Mes { get; set; } = null!;
    public string Unidade { get; set; } = null!;
    public string Login { get; set; } = null!;
    public string Secretaria { get; set; } = null!;
    public decimal MetaValor { get; set; }
    public int RealCadastro { get; set; }
    public int RealResgate { get; set; }
    public int QtdTotal { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

// ───────────────────────────────────────────────────────────────────────────
// AuditLog
// ───────────────────────────────────────────────────────────────────────────

public class SdrAuditLogResponseDto
{
    public int Id { get; set; }
    public int? UserId { get; set; }
    public string? UserName { get; set; }
    public string? UserEmail { get; set; }
    public string Action { get; set; } = null!;
    public string EntityType { get; set; } = null!;
    public int EntityId { get; set; }
    public string Summary { get; set; } = null!;
    public string? BeforeJson { get; set; }
    public string? AfterJson { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public DateTime CreatedAt { get; set; }
}
