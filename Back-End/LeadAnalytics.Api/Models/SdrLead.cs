namespace LeadAnalytics.Api.Models;

/// <summary>
/// Lead na perspectiva da SDR (secretária) — espelha a planilha "Cadastro Geral".
/// Pode chegar de 3 fontes:
///   - "cloudia"   → veio do webhook, fica em Status="pendente_revisao" até a SDR aprovar
///   - "manual"    → SDR digitou no sistema, já entra como aprovado
///   - "importado" → veio de upload em massa (CSV), já entra como aprovado
///
/// Quando a SDR aprova, Status vira "aprovado", ReviewedAt/ReviewedByUserId são preenchidos
/// e a entrada vira parte oficial do pipeline (visível em /sdr/leads-aprovados).
/// </summary>
public class SdrLead
{
    public int Id { get; set; }
    public int TenantId { get; set; }

    /// <summary>ID externo (Cloudia.data.id) — só preenchido quando Source="cloudia".</summary>
    public int? ExternalId { get; set; }

    // ─── DADOS DA PLANILHA "CADASTRO GERAL" ──────────────────
    public string Nome { get; set; } = null!;
    public string Telefone { get; set; } = null!;
    public string Tipo { get; set; } = "Cadastro";          // "Cadastro" | "Resgate"
    public string Origem { get; set; } = "Sem origem";
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

    // ─── ORIGEM E STATUS ─────────────────────────────────────
    /// <summary>"cloudia" | "manual" | "importado"</summary>
    public string Source { get; set; } = "cloudia";

    /// <summary>"pendente_revisao" | "aprovado" | "rejeitado"</summary>
    public string Status { get; set; } = "pendente_revisao";

    public DateTime? ReviewedAt { get; set; }
    public int? ReviewedByUserId { get; set; }
    public User? ReviewedByUser { get; set; }
    public string? RejectionReason { get; set; }

    // ─── CLOUDIA PROVENANCE ──────────────────────────────────
    /// <summary>JSON array com os campos que vieram preenchidos da Cloudia (chaves do form).</summary>
    public string? CloudiaFields { get; set; }
    public DateTime? CloudiaReceivedAt { get; set; }
    public string? CloudiaWebhookEvent { get; set; }

    // ─── RELACIONAMENTOS ─────────────────────────────────────
    public int? UnitId { get; set; }
    public Unit? Unit { get; set; }

    /// <summary>
    /// ID do atendente, sem FK enforced no banco — a tabela <c>attendants</c> tem IDs duplicados
    /// historicamente (8 linhas, 5 distintos), então não é seguro definir FK ainda. Quando essa
    /// integridade for corrigida, dá pra adicionar a navegação <c>Attendant?</c> e a FK.
    /// </summary>
    public int? AttendantId { get; set; }

    public int? ImportBatchId { get; set; }
    public ImportBatch? ImportBatch { get; set; }

    public ICollection<SdrConsulta> Consultas { get; set; } = [];

    // ─── AUDITORIA ───────────────────────────────────────────
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
