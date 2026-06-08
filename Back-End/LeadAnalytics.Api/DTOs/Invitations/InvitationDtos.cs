namespace LeadAnalytics.Api.DTOs.Invitations;

public class InvitationCreateDto
{
    public string Email { get; set; } = string.Empty;
    public int UnitId { get; set; }
    public string Role { get; set; } = "unit_user";

    /// <summary>Conceder acesso a todas as unidades do tenant (em vez de só a UnitId).</summary>
    public bool AllUnits { get; set; } = false;
}

public class InvitationListItemDto
{
    public int Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public int TenantId { get; set; }
    public int UnitId { get; set; }
    public string? UnitName { get; set; }
    public string Role { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? CreatedByName { get; set; }
}

public class InvitationInfoDto
{
    public string Email { get; set; } = string.Empty;
    public string? UnitName { get; set; }
    public string Role { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
}

public class InvitationAcceptDto
{
    public string IdToken { get; set; } = string.Empty;
}

public class InvitationCreateResponseDto
{
    public int Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string AcceptUrl { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
}
