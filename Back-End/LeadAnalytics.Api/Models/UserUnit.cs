using System.ComponentModel.DataAnnotations.Schema;

namespace LeadAnalytics.Api.Models;

[Table("user_units")]
public class UserUnit
{
    [Column("user_id")]
    public int UserId { get; set; }

    [Column("unit_id")]
    public int UnitId { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public User? User { get; set; }
    public Unit? Unit { get; set; }
}
