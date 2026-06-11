using System.Text.Json.Serialization;

namespace LeadAnalytics.Api.DTOs.Imports;

/// <summary>Resumo do import — DRY-RUN ou aplicação.</summary>
public class CloudiaCsvImportResultDto
{
    /// <summary>Total de linhas no CSV (já filtradas por clínica/unidade).</summary>
    [JsonPropertyName("total_rows")]
    public int TotalRows { get; set; }

    /// <summary>Linhas descartadas por dedup (mesmo telefone repetido — vence a mais recente).</summary>
    [JsonPropertyName("duplicates_removed")]
    public int DuplicatesRemoved { get; set; }

    /// <summary>Linhas únicas restantes após dedup, prontas pra match.</summary>
    [JsonPropertyName("unique_rows")]
    public int UniqueRows { get; set; }

    /// <summary>Quantas bateram em exatamente 1 lead do DB (match seguro).</summary>
    [JsonPropertyName("matched")]
    public int Matched { get; set; }

    /// <summary>Bateram em vários leads (ambíguo — não atualiza).</summary>
    [JsonPropertyName("ambiguous")]
    public int Ambiguous { get; set; }

    /// <summary>Não bateu com nenhum lead do DB (Cloudia-only — provavelmente nunca foi pra Kommo).</summary>
    [JsonPropertyName("missed")]
    public int Missed { get; set; }

    /// <summary>Sem nome ou sem data — não tentou match.</summary>
    [JsonPropertyName("invalid_input")]
    public int InvalidInput { get; set; }

    /// <summary>Quantas UPDATE rolaram de fato no banco (zero em dryRun).</summary>
    [JsonPropertyName("updated")]
    public int Updated { get; set; }

    /// <summary>Modo dry-run? (só faz match, não atualiza).</summary>
    [JsonPropertyName("dry_run")]
    public bool DryRun { get; set; }

    /// <summary>Distribuição por mês dos leads que SERIAM atualizados (validação visual).</summary>
    [JsonPropertyName("distribution_by_month")]
    public Dictionary<string, int> DistributionByMonth { get; set; } = new();

    /// <summary>Amostra dos primeiros matches (até 10) pra revisão.</summary>
    [JsonPropertyName("sample_matches")]
    public List<CloudiaCsvSampleMatchDto> SampleMatches { get; set; } = new();

    /// <summary>Amostra das duplicatas descartadas (até 5).</summary>
    [JsonPropertyName("sample_duplicates")]
    public List<string> SampleDuplicates { get; set; } = new();

    /// <summary>Amostra de pendentes (até 5).</summary>
    [JsonPropertyName("sample_missed")]
    public List<string> SampleMissed { get; set; } = new();

    /// <summary>Tempo de execução em ms.</summary>
    [JsonPropertyName("duration_ms")]
    public long DurationMs { get; set; }

    /// <summary>Id do batch criado (só preenchido em apply, não em dry-run).</summary>
    [JsonPropertyName("batch_id")]
    public int? BatchId { get; set; }
}

public class CloudiaCsvSampleMatchDto
{
    public string CsvName { get; set; } = "";
    public string DbName { get; set; } = "";
    public string DataOrigem { get; set; } = "";
    public int DbLeadId { get; set; }
}

/// <summary>Resumo de um batch (pra listagem).</summary>
public class CloudiaImportBatchDto
{
    public int Id { get; set; }

    [JsonPropertyName("unit_id")]
    public int UnitId { get; set; }

    public string? Filename { get; set; }

    public string Status { get; set; } = "";

    [JsonPropertyName("total_rows")]
    public int TotalRows { get; set; }

    public int Matched { get; set; }

    public int Updated { get; set; }

    [JsonPropertyName("update_lead_type")]
    public bool UpdateLeadType { get; set; }

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("uploaded_by_user_id")]
    public int? UploadedByUserId { get; set; }

    [JsonPropertyName("reverted_at")]
    public DateTime? RevertedAt { get; set; }

    [JsonPropertyName("reverted_by_user_id")]
    public int? RevertedByUserId { get; set; }
}

public class CloudiaRevertResultDto
{
    [JsonPropertyName("batch_id")]
    public int BatchId { get; set; }

    [JsonPropertyName("leads_restored")]
    public int LeadsRestored { get; set; }

    [JsonPropertyName("duration_ms")]
    public long DurationMs { get; set; }
}
