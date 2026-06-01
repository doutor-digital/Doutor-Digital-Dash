using System.ComponentModel.DataAnnotations.Schema;

namespace LeadAnalytics.Api.Models;

/// <summary>
/// Trilha de alteração de entidades: quem mudou o quê (antes → depois). Gerado
/// automaticamente no <c>SaveChangesAsync</c> do <see cref="Data.AppDbContext"/>
/// para um conjunto whitelisted de entidades. Visível a super_admin / analista_ti.
/// </summary>
[Table("entity_change_logs")]
public class EntityChangeLog
{
    [Column("id")]
    public long Id { get; set; }

    [Column("user_id")]
    public int? UserId { get; set; }

    [Column("email")]
    public string? Email { get; set; }

    [Column("role")]
    public string? Role { get; set; }

    [Column("tenant_id")]
    public int? TenantId { get; set; }

    /// <summary>Nome da entidade (ex.: "Lead", "User", "Unit", "Invitation").</summary>
    [Column("entity_type")]
    public string EntityType { get; set; } = string.Empty;

    [Column("entity_id")]
    public string? EntityId { get; set; }

    /// <summary>Added / Modified / Deleted.</summary>
    [Column("action")]
    public string Action { get; set; } = string.Empty;

    /// <summary>JSON com os campos alterados: { campo: { from, to } }.</summary>
    [Column("changes_json")]
    public string? ChangesJson { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
