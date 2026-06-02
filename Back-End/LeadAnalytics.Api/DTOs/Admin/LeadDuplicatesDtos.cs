namespace LeadAnalytics.Api.DTOs.Admin;

/// <summary>
/// Relatório (dry-run) de leads duplicados por (TenantId, telefone normalizado).
/// Em cada grupo, o lead "mais avançado" (com pagamento/agendamento/maior valor/etapa)
/// é MANTIDO; os demais entram em <see cref="LeadDuplicateGroupDto.DeleteLeadIds"/>.
/// </summary>
public class LeadDuplicatesReportDto
{
    public bool DryRun { get; set; } = true;
    public int GroupsFound { get; set; }
    public int LeadsToDelete { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
    public int TotalPages { get; set; } = 1;
    public List<LeadDuplicateGroupDto> Groups { get; set; } = [];
}

public class LeadDuplicateGroupDto
{
    public int TenantId { get; set; }
    public string PhoneNormalized { get; set; } = string.Empty;
    public int Count { get; set; }

    // Lead mantido (o mais avançado).
    public int KeepLeadId { get; set; }
    public string KeepName { get; set; } = string.Empty;
    public string? KeepStage { get; set; }
    public bool KeepHasPayment { get; set; }
    public bool KeepHasAppointment { get; set; }
    public decimal? KeepPrice { get; set; }
    public DateTime KeepCreatedAt { get; set; }

    // Leads que serão apagados.
    public List<int> DeleteLeadIds { get; set; } = [];
    public List<string> DeleteNames { get; set; } = [];
}

public class StartLeadDuplicateDeleteJobRequest
{
    public int? TenantId { get; set; }
    public bool IgnoreTenant { get; set; }
    public int? BatchSize { get; set; }
    /// <summary>Marcar os duplicados como "DUPLICADO" na Kommo antes de apagar (default: true).</summary>
    public bool TagInKommo { get; set; } = true;
}

public class StartLeadDuplicateDeleteJobResponse
{
    public string JobId { get; set; } = string.Empty;
    public DuplicateDeleteJobStatus Status { get; set; }
}

/// <summary>Progresso de um job de exclusão de leads duplicados (em background).</summary>
public class LeadDuplicateDeleteJobDto
{
    public string Id { get; set; } = string.Empty;
    public DuplicateDeleteJobStatus Status { get; set; }

    public int? TenantId { get; set; }
    public bool IgnoreTenant { get; set; }
    public int BatchSize { get; set; }
    public bool TagInKommo { get; set; }

    public int LeadsToDeleteTotal { get; set; }
    public int LeadsDeleted { get; set; }
    public int TaggedInKommo { get; set; }
    public int TagFailures { get; set; }
    public int GroupsFound { get; set; }
    public int BatchesExecuted { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }

    public string? Error { get; set; }
    public string CreatedBy { get; set; } = string.Empty;

    public int ProgressPct =>
        LeadsToDeleteTotal <= 0
            ? 0
            : (int)Math.Min(100, Math.Round(LeadsDeleted * 100.0 / LeadsToDeleteTotal));
}
