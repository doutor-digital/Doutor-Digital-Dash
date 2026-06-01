namespace LeadAnalytics.Api.Service;

/// <summary>
/// Fonte única de verdade dos papéis (roles) do sistema. Os papéis são armazenados
/// como string em <c>users.role</c> / <c>invitations.role</c>; estes helpers
/// normalizam (lowercase + variantes com hífen) para parar de repetir arrays de
/// string espalhados pelos controllers/services.
/// </summary>
public static class Roles
{
    public const string SuperAdmin = "super_admin";
    public const string AnalistaTi = "analista_ti";
    public const string TrafegoPago = "trafego_pago";
    public const string Sdr = "sdr";
    public const string Manager = "manager";
    public const string UnitUser = "unit_user";

    private static string Norm(string? role) => (role ?? string.Empty).Trim().ToLowerInvariant();

    /// <summary>super_admin (aceita variantes super-admin / superadmin).</summary>
    public static bool IsSuperAdmin(string? role)
    {
        var r = Norm(role);
        return r is "super_admin" or "super-admin" or "superadmin";
    }

    /// <summary>analista_ti (aceita variante analista-ti).</summary>
    public static bool IsAnalistaTi(string? role)
    {
        var r = Norm(role);
        return r is "analista_ti" or "analista-ti";
    }

    /// <summary>
    /// Nível administrativo total: super_admin OU analista_ti. Usado para acesso
    /// global a unidades e para enxergar os logs/auditoria avançada.
    /// </summary>
    public static bool IsAdminLevel(string? role) => IsSuperAdmin(role) || IsAnalistaTi(role);

    /// <summary>trafego_pago: acesso somente-leitura (só visualiza números).</summary>
    public static bool IsReadOnly(string? role)
    {
        var r = Norm(role);
        return r is "trafego_pago" or "trafego-pago";
    }

    /// <summary>Quem pode criar convites.</summary>
    public static bool CanInvite(string? role)
    {
        var r = Norm(role);
        return IsAdminLevel(role) || r is "sdr" or "manager";
    }

    /// <summary>Papéis válidos ao criar um convite.</summary>
    public static readonly string[] ValidInviteRoles =
        { UnitUser, Sdr, Manager, TrafegoPago, AnalistaTi };

    /// <summary>Normaliza um papel para a forma canônica (com underscore).</summary>
    public static string Canonical(string? role)
    {
        var r = Norm(role);
        if (IsSuperAdmin(r)) return SuperAdmin;
        if (IsAnalistaTi(r)) return AnalistaTi;
        if (IsReadOnly(r)) return TrafegoPago;
        return r;
    }

    public static bool IsValidInviteRole(string? role)
    {
        var r = Canonical(role);
        return Array.Exists(ValidInviteRoles, x => x == r);
    }
}
