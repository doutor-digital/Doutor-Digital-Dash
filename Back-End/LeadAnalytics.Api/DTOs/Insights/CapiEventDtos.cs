namespace LeadAnalytics.Api.DTOs.Insights;

public class CapiEventDto
{
    public string Id { get; set; } = "";
    public string EventName { get; set; } = "";
    public string Status { get; set; } = "received";
    public DateTime EventTime { get; set; }
    public DateTime? SentAt { get; set; }
    public string? Source { get; set; }
    public string? PixelId { get; set; }
    public int? LeadId { get; set; }
    public string? LeadName { get; set; }
    public string? Phone { get; set; }
    public bool HasEmailHash { get; set; }
    public bool HasPhoneHash { get; set; }
    public bool HasIp { get; set; }
    public bool HasFbp { get; set; }
    public bool HasFbc { get; set; }
    public double? EmqScore { get; set; }
    public string? MatchQuality { get; set; }
    public bool IsDeduped { get; set; }
    public string? DedupedWith { get; set; }
    public int RetryCount { get; set; }
    public string? ErrorMessage { get; set; }
    public string? FbtraceId { get; set; }
    public decimal? Value { get; set; }
    public string? Currency { get; set; }
    public string? Campaign { get; set; }
    public string? AdId { get; set; }
}

public class CapiEventListDto
{
    public int Total { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public List<CapiEventDto> Items { get; set; } = new();
    public CapiEventStatsDto Stats { get; set; } = new();
}

public class CapiEventStatsDto
{
    public int Received { get; set; }
    public int Sent { get; set; }
    public int Failed { get; set; }
    public int Deduped { get; set; }
    public int Pending { get; set; }
    public double SuccessRate { get; set; }
    public double DedupRate { get; set; }
    public double AverageEmqScore { get; set; }
    public Dictionary<string, int> ByEventName { get; set; } = new();
    public Dictionary<string, int> ByStatus { get; set; } = new();
    public List<CapiTimeBucketDto> Timeline { get; set; } = new();
}

public class CapiTimeBucketDto
{
    public DateTime Bucket { get; set; }
    public int Sent { get; set; }
    public int Failed { get; set; }
    public int Deduped { get; set; }
}

public class PixelHealthDto
{
    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }
    public int TotalEvents { get; set; }
    public double EmailHashCoverage { get; set; }
    public double PhoneHashCoverage { get; set; }
    public double IpCoverage { get; set; }
    public double FbpCoverage { get; set; }
    public double FbcCoverage { get; set; }
    public double AverageEmqScore { get; set; }
    public double DeduplicationRate { get; set; }
    public List<PixelHealthByUnitDto> ByUnit { get; set; } = new();
    public List<PixelHealthAlertDto> Alerts { get; set; } = new();
}

public class PixelHealthByUnitDto
{
    public int UnitId { get; set; }
    public string UnitName { get; set; } = "";
    public int TotalEvents { get; set; }
    public double EmqScore { get; set; }
    public double Coverage { get; set; }
}

public class PixelHealthAlertDto
{
    public string Severity { get; set; } = "warning";
    public string Title { get; set; } = "";
    public string Message { get; set; } = "";
}
