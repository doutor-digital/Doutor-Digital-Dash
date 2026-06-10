using System.ComponentModel.DataAnnotations.Schema;

namespace LeadAnalytics.Api.Models;

[Table("recovery_attempts")]
public class RecoveryAttempt
{
    [Column("id")]
    public int Id { get; set; }

    [Column("lead_id")]
    public int LeadId { get; set; }

    public Lead? Lead { get; set; }

    [Column("tenant_id")]
    public int TenantId { get; set; }

    // whatsapp | call | email | visit | other
    [Column("method")]
    public string Method { get; set; } = "whatsapp";

    // no_answer | scheduled | recovered | lost | follow_up
    [Column("outcome")]
    public string Outcome { get; set; } = "no_answer";

    [Column("notes")]
    public string? Notes { get; set; }

    [Column("attendant_id")]
    public int? AttendantId { get; set; }

    [Column("created_by_user_id")]
    public int? CreatedByUserId { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Id do evento de mudança de campo na Kommo (ULID) quando a tentativa veio do backfill
    /// do campo "Tentativas de resgastes". Chave de dedup: reexecutar não duplica. Null p/ manual.
    /// </summary>
    [Column("kommo_event_id")]
    public string? KommoEventId { get; set; }

    /// <summary>Procedência: "events_api" (backfill do campo) | "manual". Define a data confiável.</summary>
    [Column("entry_source")]
    public string EntrySource { get; set; } = "manual";
}
