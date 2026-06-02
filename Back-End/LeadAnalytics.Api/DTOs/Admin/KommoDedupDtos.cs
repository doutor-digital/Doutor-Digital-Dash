namespace LeadAnalytics.Api.DTOs.Admin;

/// <summary>
/// Job que acha duplicados lendo a API da Kommo AO VIVO (não o nosso banco) e marca
/// os duplicados com a tag "DUPLICADO" na própria Kommo. Não apaga (a API não deixa);
/// depois o usuário filtra a tag na Kommo e apaga em massa pela tela deles.
/// </summary>
public class KommoDedupJobDto
{
    public string Id { get; set; } = string.Empty;
    public DuplicateDeleteJobStatus Status { get; set; }

    public int UnitId { get; set; }
    public int? TenantId { get; set; }
    /// <summary>"phone" ou "name".</summary>
    public string Mode { get; set; } = "phone";
    /// <summary>false = só busca/preview; true = aplica a tag DUPLICADO na Kommo.</summary>
    public bool Apply { get; set; }

    public int LeadsFetched { get; set; }
    public int GroupsFound { get; set; }
    public int LeadsToTag { get; set; }
    public int Tagged { get; set; }
    public int Confirmed { get; set; }
    public int Failed { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }

    public string? Error { get; set; }
    public string CreatedBy { get; set; } = string.Empty;

    public int ProgressPct =>
        LeadsToTag <= 0
            ? (Status == DuplicateDeleteJobStatus.Completed ? 100 : 0)
            : (int)Math.Min(100, Math.Round((Tagged + Failed) * 100.0 / LeadsToTag));
}

public class StartKommoDedupRequest
{
    public int UnitId { get; set; }
    public string Mode { get; set; } = "phone";
    /// <summary>false = só busca/preview; true = aplica a tag DUPLICADO.</summary>
    public bool Apply { get; set; }
}

public class StartKommoDedupResponse
{
    public string JobId { get; set; } = string.Empty;
    public DuplicateDeleteJobStatus Status { get; set; }
}
