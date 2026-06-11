using System.ComponentModel.DataAnnotations.Schema;

namespace LeadAnalytics.Api.Models;

/// <summary>
/// Job de PATCH em massa de campos customizados no Kommo, baseado num batch de import
/// já aplicado. Roda em background (Channel + BackgroundService), atualizado via polling.
/// </summary>
[Table("cloudia_kommo_jobs")]
public class CloudiaKommoJob
{
    [Column("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [Column("batch_id")]
    public int BatchId { get; set; }

    [Column("unit_id")]
    public int UnitId { get; set; }

    [Column("tenant_id")]
    public int TenantId { get; set; }

    /// <summary>"queued" | "running" | "completed" | "failed" | "cancelling" | "cancelled"</summary>
    [Column("status")]
    public string Status { get; set; } = "queued";

    [Column("total")]
    public int Total { get; set; }

    [Column("processed")]
    public int Processed { get; set; }

    [Column("succeeded")]
    public int Succeeded { get; set; }

    [Column("failed")]
    public int Failed { get; set; }

    /// <summary>
    /// Lista dos campos do Kommo a preencher. Estrutura:
    /// <c>["tipo_lead","data_criacao","origem","interacao","motivo","tipo_resgate","data_agendamento","sexo","qualificacao","observacao"]</c>
    /// </summary>
    [Column("fields_json")]
    public string FieldsJson { get; set; } = "[]";

    [Column("error_message")]
    public string? ErrorMessage { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("started_at")]
    public DateTime? StartedAt { get; set; }

    [Column("finished_at")]
    public DateTime? FinishedAt { get; set; }

    [Column("created_by_user_id")]
    public int? CreatedByUserId { get; set; }

    [Column("cancel_requested")]
    public bool CancelRequested { get; set; }
}
