namespace LeadAnalytics.Api.DTOs.Admin;

public class LoginSessionDto
{
    public long Id { get; set; }
    public int UserId { get; set; }
    public string? Email { get; set; }
    public string? UserName { get; set; }
    public string? Role { get; set; }
    public int? TenantId { get; set; }
    public string? AuthMethod { get; set; }
    public string? Ip { get; set; }
    public string? Device { get; set; }
    public string? GeoCountry { get; set; }
    public string? GeoRegion { get; set; }
    public string? GeoCity { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public double? Accuracy { get; set; }
    public bool GeoConsent { get; set; }
    public DateTime? GeoConsentAt { get; set; }
    public DateTime LoginAt { get; set; }
    public DateTime LastSeenAt { get; set; }
    public long ActiveSeconds { get; set; }
    /// <summary>Minutos ativos (ActiveSeconds / 60), arredondado.</summary>
    public int ActiveMinutes { get; set; }
    public DateTime? EndedAt { get; set; }
    public string? EndReason { get; set; }
    public bool IsActive { get; set; }
}

public class LoginSessionPageDto
{
    public List<LoginSessionDto> Items { get; set; } = new();
    public int Total { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}

public class EntityChangeDto
{
    public long Id { get; set; }
    public int? UserId { get; set; }
    public string? Email { get; set; }
    public string? Role { get; set; }
    public int? TenantId { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public string? EntityId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? ChangesJson { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class EntityChangePageDto
{
    public List<EntityChangeDto> Items { get; set; } = new();
    public int Total { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}

/// <summary>Usuário que consentiu compartilhar a localização + última posição conhecida.</summary>
public class LocationConsentDto
{
    public int UserId { get; set; }
    public string? Name { get; set; }
    public string? Email { get; set; }
    public string? Role { get; set; }
    public DateTime? ConsentAt { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public double? Accuracy { get; set; }
    public DateTime? LastSeenAt { get; set; }
    public string? GeoCity { get; set; }
}
