using System.ComponentModel.DataAnnotations.Schema;

namespace LeadAnalytics.Api.Models;

/// <summary>
/// Fila persistente de eventos webhook (entrada).
///
/// Idempotência: índice único composto em (Provider, ContactId, StageTo, OccurredAt).
/// Webhook duplicado da Cloudia → INSERT ON CONFLICT DO NOTHING — não cria registro.
///
/// Status:
///   pending    → aguardando worker
///   processing → worker pegou, ainda rodando
///   done       → processado com sucesso
///   failed     → falhou todas as tentativas (Attempts >= MaxAttempts)
/// </summary>
[Table("webhook_envelopes")]
public class WebhookEnvelope
{
    [Column("id")]
    public long Id { get; set; }

    /// <summary>"cloudia", "kommo", "meta" etc.</summary>
    [Column("provider")]
    public string Provider { get; set; } = "cloudia";

    /// <summary>ID do contato no provedor (chave de idempotência).</summary>
    [Column("contact_id")]
    public string ContactId { get; set; } = string.Empty;

    /// <summary>tenant_id resolvido a partir do payload (clinic_id da Cloudia).</summary>
    [Column("tenant_id")]
    public int? TenantId { get; set; }

    /// <summary>Etapa anterior (quando o provedor envia).</summary>
    [Column("stage_from")]
    public string? StageFrom { get; set; }

    /// <summary>Nova etapa (gatilho da ação) — chave de idempotência.</summary>
    [Column("stage_to")]
    public string StageTo { get; set; } = string.Empty;

    /// <summary>Quando o evento ocorreu na Cloudia (timestamp do payload).</summary>
    [Column("occurred_at")]
    public DateTime OccurredAt { get; set; }

    [Column("received_at")]
    public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;

    [Column("processed_at")]
    public DateTime? ProcessedAt { get; set; }

    [Column("status")]
    public string Status { get; set; } = "pending";

    [Column("attempts")]
    public int Attempts { get; set; }

    /// <summary>Próxima tentativa (controla backoff).</summary>
    [Column("next_attempt_at")]
    public DateTime? NextAttemptAt { get; set; }

    [Column("last_error")]
    public string? LastError { get; set; }

    /// <summary>Payload bruto pra reprocessar / forensics.</summary>
    [Column("payload", TypeName = "jsonb")]
    public string Payload { get; set; } = "{}";
}
