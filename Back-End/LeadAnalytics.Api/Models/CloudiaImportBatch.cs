using System.ComponentModel.DataAnnotations.Schema;

namespace LeadAnalytics.Api.Models;

/// <summary>
/// Cada execução do import Cloudia CSV vira um batch. Guarda o snapshot
/// dos valores ANTES do UPDATE (per-lead) pra permitir revert.
/// </summary>
[Table("cloudia_import_batches")]
public class CloudiaImportBatch
{
    [Column("id")]
    public int Id { get; set; }

    [Column("unit_id")]
    public int UnitId { get; set; }

    [Column("tenant_id")]
    public int TenantId { get; set; }

    [Column("filename")]
    public string? Filename { get; set; }

    [Column("uploaded_by_user_id")]
    public int? UploadedByUserId { get; set; }

    /// <summary>"applied" | "reverted"</summary>
    [Column("status")]
    public string Status { get; set; } = "applied";

    [Column("total_rows")]
    public int TotalRows { get; set; }

    [Column("matched")]
    public int Matched { get; set; }

    [Column("updated")]
    public int Updated { get; set; }

    [Column("update_lead_type")]
    public bool UpdateLeadType { get; set; }

    /// <summary>
    /// Array JSON dos snapshots PRÉ-UPDATE de cada lead afetado:
    /// <c>[{"id":123,"oca":"2025-02-03T08:18Z","leadType":null}, …]</c>
    /// Usado pra reverter o batch.
    /// </summary>
    [Column("snapshot_json")]
    public string SnapshotJson { get; set; } = "[]";

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("reverted_at")]
    public DateTime? RevertedAt { get; set; }

    [Column("reverted_by_user_id")]
    public int? RevertedByUserId { get; set; }

    /// <summary>
    /// Array JSON dos dados do CSV por lead (necessários pra Kommo PATCH posterior):
    /// <c>[{"id":123,"externalId":13016462,"nome":"Edileusa","tipo":"Cadastro","origem":"Campanha Meta (Facebook)",…}, …]</c>
    /// Vazio em batches antigos (pré-feature).
    /// </summary>
    [Column("csv_data_json")]
    public string CsvDataJson { get; set; } = "[]";
}

