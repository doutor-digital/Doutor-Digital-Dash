using System.ComponentModel.DataAnnotations.Schema;

namespace LeadAnalytics.Api.Models;

/// <summary>
/// Uma sessão de login (uma “entrada” do usuário). Registra de onde acessou
/// (IP + cidade via GeoIP, e localização precisa por GPS quando consentida) e
/// quanto tempo ficou ativo (heartbeat acumulando <see cref="ActiveSeconds"/>).
/// Alimenta o controle de log avançado visível a super_admin / analista_ti.
/// </summary>
[Table("login_sessions")]
public class LoginSession
{
    [Column("id")]
    public long Id { get; set; }

    [Column("user_id")]
    public int UserId { get; set; }

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

    /// <summary>Resumo legível do dispositivo/navegador (parse simples do User-Agent).</summary>
    [Column("device")]
    public string? Device { get; set; }

    // ─── Geolocalização por IP (automática, aproximada) ───────────────────
    [Column("geo_country")]
    public string? GeoCountry { get; set; }

    [Column("geo_region")]
    public string? GeoRegion { get; set; }

    [Column("geo_city")]
    public string? GeoCity { get; set; }

    // ─── Geolocalização precisa por GPS (só com consentimento) ────────────
    [Column("latitude")]
    public double? Latitude { get; set; }

    [Column("longitude")]
    public double? Longitude { get; set; }

    [Column("accuracy")]
    public double? Accuracy { get; set; }

    [Column("geo_consent")]
    public bool GeoConsent { get; set; } = false;

    [Column("geo_consent_at")]
    public DateTime? GeoConsentAt { get; set; }

    // ─── Tempo de sessão ──────────────────────────────────────────────────
    [Column("login_at")]
    public DateTime LoginAt { get; set; } = DateTime.UtcNow;

    [Column("last_seen_at")]
    public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;

    /// <summary>Segundos de atividade real acumulados via heartbeat.</summary>
    [Column("active_seconds")]
    public long ActiveSeconds { get; set; } = 0;

    [Column("ended_at")]
    public DateTime? EndedAt { get; set; }

    [Column("end_reason")]
    public string? EndReason { get; set; }

    [Column("is_active")]
    public bool IsActive { get; set; } = true;
}
