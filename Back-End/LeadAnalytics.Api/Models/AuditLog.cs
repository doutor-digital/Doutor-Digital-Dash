using System.ComponentModel.DataAnnotations.Schema;

namespace LeadAnalytics.Api.Models;

[Table("audit_logs")]
public class AuditLog
{
    [Column("id")]
    public long Id { get; set; }

    [Column("user_id")]
    public int? UserId { get; set; }

    [Column("email")]
    public string? Email { get; set; }

    [Column("user_name")]
    public string? UserName { get; set; }

    [Column("role")]
    public string? Role { get; set; }

    [Column("tenant_id")]
    public int? TenantId { get; set; }

    [Column("auth_method")]
    public string? AuthMethod { get; set; }

    [Column("ip")]
    public string? Ip { get; set; }

    [Column("user_agent")]
    public string? UserAgent { get; set; }

    [Column("method")]
    public string Method { get; set; } = "GET";

    [Column("path")]
    public string Path { get; set; } = string.Empty;

    [Column("query_string")]
    public string? QueryString { get; set; }

    [Column("status_code")]
    public int StatusCode { get; set; }

    [Column("duration_ms")]
    public int DurationMs { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
