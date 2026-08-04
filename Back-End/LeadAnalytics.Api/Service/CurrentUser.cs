using System.Linq;
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

    /// <summary>
    /// É a conta dona do produto — a única que pode alterar de onde cada KPI é puxado.
    ///
    /// Mapear KPI para etapa muda o número que TODA a rede enxerga, e um mapeamento
    /// errado não parece erro: parece queda de desempenho. Por isso essa configuração
    /// não segue papel (analista_ti pode ser concedido a qualquer pessoa) e sim uma
    /// conta nominal, definida em <c>Security:OwnerEmail</c>.
    /// </summary>
    bool IsOwner { get; }
}

public class CurrentUser(IHttpContextAccessor accessor, IConfiguration config) : ICurrentUser
{
    private readonly IHttpContextAccessor _accessor = accessor;
    private readonly IConfiguration _config = config;

    /// <summary>
    /// Contas responsáveis, separadas por vírgula. Fica em configuração para que incluir
    /// alguém seja uma variável de ambiente, e não um deploy de código.
    /// </summary>
    private IEnumerable<string> OwnerEmails =>
        (_config["Security:OwnerEmail"] ?? "doutordigitalconsultoria@gmail.com")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public bool IsOwner =>
        IsAuthenticated
        && !string.IsNullOrWhiteSpace(Email)
        && OwnerEmails.Any(e => string.Equals(Email!.Trim(), e, StringComparison.OrdinalIgnoreCase));

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
