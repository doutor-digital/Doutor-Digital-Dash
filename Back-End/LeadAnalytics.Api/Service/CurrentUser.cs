using System.Security.Claims;

namespace LeadAnalytics.Api.Service;

public interface ICurrentUser
{
    int? UserId { get; }
    int? TenantId { get; }
    string? Role { get; }
    string? Email { get; }
    bool IsSuperAdmin { get; }
    /// <summary>super_admin OU analista_ti — acesso administrativo total + logs avançados.</summary>
    bool IsAdminLevel { get; }
    /// <summary>trafego_pago — acesso somente-leitura.</summary>
    bool IsReadOnly { get; }
    bool IsAuthenticated { get; }
    /// <summary>Id da sessão de login (claim <c>sid</c>), quando presente.</summary>
    long? SessionId { get; }
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

    public string? Email => Principal?.FindFirst(ClaimTypes.Email)?.Value;

    public bool IsSuperAdmin => Roles.IsSuperAdmin(Role);

    public bool IsAdminLevel => Roles.IsAdminLevel(Role);

    public bool IsReadOnly => Roles.IsReadOnly(Role);

    public long? SessionId =>
        long.TryParse(Principal?.FindFirst("sid")?.Value, out var id) ? id : null;
}
