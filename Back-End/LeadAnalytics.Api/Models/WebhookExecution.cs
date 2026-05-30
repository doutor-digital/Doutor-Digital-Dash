using System.ComponentModel.DataAnnotations.Schema;

namespace LeadAnalytics.Api.Models;

/// <summary>
/// Auditoria detalhada de cada execução de webhook recebida pela API.
/// Usado pelo painel <c>/webhooks-monitor</c> pra debugar o que chegou,
/// o que foi parseado e o que foi persistido — útil quando "lead apareceu
/// na Kommo mas não no dashboard".
/// </summary>
[Table("webhook_executions")]
public class WebhookExecution
{
    [Column("id")] public long Id { get; set; }

    /// <summary>"kommo", "meta" etc.</summary>
    [Column("provider")] public string Provider { get; set; } = null!;

    /// <summary>Slug usado na URL (ex.: "doutor-hernia-araguaina"). Pode estar inválido.</summary>
    [Column("slug")] public string? Slug { get; set; }

    /// <summary>Unit.Id resolvida. Null = slug não bateu com nenhuma unidade.</summary>
    [Column("unit_id")] public int? UnitId { get; set; }
    /// <summary>Tenant (ClinicId) resolvido pela unidade.</summary>
    [Column("tenant_id")] public int? TenantId { get; set; }

    [Column("kommo_account_id")] public string? KommoAccountId { get; set; }
    [Column("kommo_subdomain")] public string? KommoSubdomain { get; set; }

    [Column("received_at")] public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;
    [Column("duration_ms")] public int DurationMs { get; set; }

    [Column("method")] public string Method { get; set; } = "POST";
    [Column("path")] public string Path { get; set; } = string.Empty;
    [Column("ip")] public string? Ip { get; set; }
    [Column("user_agent")] public string? UserAgent { get; set; }
    [Column("content_type")] public string? ContentType { get; set; }
    [Column("content_length")] public long? ContentLength { get; set; }

    /// <summary>"success", "failed", "ignored" (slug não existe / unidade inativa / formato inesperado).</summary>
    [Column("status")] public string Status { get; set; } = "success";
    [Column("status_code")] public int StatusCode { get; set; }
    [Column("success")] public bool Success { get; set; }

    [Column("error_message")] public string? ErrorMessage { get; set; }
    [Column("error_stack")] public string? ErrorStack { get; set; }

    /// <summary>CSV das top-level keys do form (ex.: "leads,account,unsorted").</summary>
    [Column("form_keys")] public string? FormKeys { get; set; }
    [Column("form_key_count")] public int FormKeyCount { get; set; }

    /// <summary>Corpo bruto do form-urlencoded. Truncado em 50KB.</summary>
    [Column("raw_payload")] public string? RawPayload { get; set; }
    [Column("payload_truncated")] public bool PayloadTruncated { get; set; }

    [Column("events_parsed")] public int EventsParsed { get; set; }
    /// <summary>JSON do breakdown por entityType:action (ex.: {"lead:add":1,"unsorted:add":1}).</summary>
    [Column("events_summary", TypeName = "jsonb")] public string? EventsSummary { get; set; }
    [Column("leads_persisted")] public int LeadsPersisted { get; set; }

    /// <summary>JSON do que devolvemos pra Kommo.</summary>
    [Column("response_body")] public string? ResponseBody { get; set; }
}
