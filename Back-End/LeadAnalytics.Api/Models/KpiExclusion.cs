using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace LeadAnalytics.Api.Models;

/// <summary>
/// Exclusão manual de um lead de um KPI específico — o admin marca "não contar"
/// no drill-down quando a SDR errou (moveu de etapa fora do horário, clicou errado,
/// duplicidade, etc.). O lead continua existindo no banco; só não aparece no card.
///
/// Chave natural: (TenantId, UnitId, KpiKey, LeadId) — único, então marcar 2x
/// no mesmo lead/KPI é idempotente. Restaurar = deletar a linha.
///
/// Por enquanto só o KPI "agendados" usa essa tabela, mas o modelo é genérico
/// pra estender pra outros cards (no_show, tratamentos, etc.) sem nova migration.
/// </summary>
[Table("kpi_exclusions")]
public class KpiExclusion
{
    [Column("id")]
    public int Id { get; set; }

    [Column("tenant_id")]
    public int TenantId { get; set; }

    [Column("unit_id")]
    public int? UnitId { get; set; }

    /// <summary>"agendados", "no_show", "tratamentos"... (alinhado com kpiKey do front).</summary>
    [Required, MaxLength(64)]
    [Column("kpi_key")]
    public string KpiKey { get; set; } = "";

    [Column("lead_id")]
    public int LeadId { get; set; }

    public Lead? Lead { get; set; }

    [Column("excluded_at")]
    public DateTime ExcludedAt { get; set; } = DateTime.UtcNow;

    [Column("excluded_by_user_id")]
    public int? ExcludedByUserId { get; set; }

    [MaxLength(500)]
    [Column("reason")]
    public string? Reason { get; set; }
}
