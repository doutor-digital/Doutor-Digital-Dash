namespace LeadAnalytics.Api.DTOs.Audit;

public class AuditLogItemDto
{
    public long Id { get; set; }
    public int? UserId { get; set; }
    public string? Email { get; set; }
    public string? UserName { get; set; }
    public string? Role { get; set; }
    public int? TenantId { get; set; }
    public string? AuthMethod { get; set; }
    public string? Ip { get; set; }
    public string? UserAgent { get; set; }
    public string Method { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string? QueryString { get; set; }
    public int StatusCode { get; set; }
    public int DurationMs { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class AuditLogPageDto
{
    public List<AuditLogItemDto> Items { get; set; } = new();
    public int Total { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}
