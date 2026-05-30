namespace LeadAnalytics.Api.DTOs.Webhooks;

/// <summary>Resumo de uma execução de webhook, usado na listagem.</summary>
public class WebhookExecutionSummaryDto
{
    public long Id { get; set; }
    public string Provider { get; set; } = null!;
    public string? Slug { get; set; }
    public int? UnitId { get; set; }
    public string? UnitName { get; set; }
    public int? TenantId { get; set; }
    public string? KommoSubdomain { get; set; }
    public DateTime ReceivedAt { get; set; }
    public int DurationMs { get; set; }
    public string Status { get; set; } = null!;
    public int StatusCode { get; set; }
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public int EventsParsed { get; set; }
    public int LeadsPersisted { get; set; }
    public string? FormKeys { get; set; }
    public string? Ip { get; set; }
}

/// <summary>Detalhe completo, incluindo payload bruto.</summary>
public class WebhookExecutionDetailDto : WebhookExecutionSummaryDto
{
    public string Method { get; set; } = "POST";
    public string Path { get; set; } = null!;
    public string? UserAgent { get; set; }
    public string? ContentType { get; set; }
    public long? ContentLength { get; set; }
    public string? KommoAccountId { get; set; }
    public string? RawPayload { get; set; }
    public bool PayloadTruncated { get; set; }
    public string? EventsSummary { get; set; }
    public string? ResponseBody { get; set; }
    public string? ErrorStack { get; set; }
    public int FormKeyCount { get; set; }
}

/// <summary>KPIs do topo do painel.</summary>
public class WebhookExecutionStatsDto
{
    public int Total { get; set; }
    public int Success { get; set; }
    public int Failed { get; set; }
    public int Ignored { get; set; }
    public int LeadsPersisted { get; set; }
    public int AvgDurationMs { get; set; }
    public DateTime? LastFailureAt { get; set; }
    public DateTime? LastSuccessAt { get; set; }
}

/// <summary>Resposta paginada.</summary>
public class WebhookExecutionListDto
{
    public List<WebhookExecutionSummaryDto> Items { get; set; } = new();
    public int Total { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}
