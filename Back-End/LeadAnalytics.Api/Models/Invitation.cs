using System.ComponentModel.DataAnnotations.Schema;

namespace LeadAnalytics.Api.Models;

[Table("invitations")]
public class Invitation
{
    [Column("id")]
    public int Id { get; set; }

    [Column("email")]
    public string Email { get; set; } = null!;

    [Column("tenant_id")]
    public int TenantId { get; set; }

    [Column("unit_id")]
    public int UnitId { get; set; }

    [Column("role")]
    public string Role { get; set; } = "unit_user";

    [Column("token_hash")]
    public string TokenHash { get; set; } = null!;

    [Column("expires_at")]
    public DateTime ExpiresAt { get; set; }

    [Column("accepted_at")]
    public DateTime? AcceptedAt { get; set; }

    [Column("revoked_at")]
    public DateTime? RevokedAt { get; set; }

    [Column("created_by_user_id")]
    public int CreatedByUserId { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
