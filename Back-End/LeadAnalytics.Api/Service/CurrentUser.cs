using System.Security.Claims;

namespace LeadAnalytics.Api.Service;

public interface ICurrentUser
{
    int? UserId { get; }
    int? TenantId { get; }
    string? Role { get; }
    bool IsSuperAdmin { get; }
    bool IsAuthenticated { get; }
}

public class CurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private readonly IHttpContextAccessor _accessor = accessor;

    private ClaimsPrincipal? Principal => _accessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated ?? false;

    public int? UserId =>
        int.TryParse(Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : null;

    public int? TenantId =>
        int.TryParse(Principal?.FindFirst("tenant_id")?.Value, out var id) ? id : null;

    public string? Role => Principal?.FindFirst(ClaimTypes.Role)?.Value;

    public bool IsSuperAdmin =>
        string.Equals(Role, "super_admin", StringComparison.OrdinalIgnoreCase)
        || string.Equals(Role, "super-admin", StringComparison.OrdinalIgnoreCase)
        || string.Equals(Role, "superadmin", StringComparison.OrdinalIgnoreCase);
}
