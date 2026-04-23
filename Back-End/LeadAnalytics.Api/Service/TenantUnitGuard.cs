using LeadAnalytics.Api.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LeadAnalytics.Api.Service;

/// <summary>
/// Valida se a unidade/clinica informada no request pertence ao tenant autenticado.
/// Retorna null quando OK; caso contrário um IActionResult para o controller devolver.
/// </summary>
public class TenantUnitGuard(
    AppDbContext db,
    ICurrentUser currentUser,
    ILogger<TenantUnitGuard> logger)
{
    private readonly AppDbContext _db = db;
    private readonly ICurrentUser _currentUser = currentUser;
    private readonly ILogger<TenantUnitGuard> _logger = logger;

    /// <summary>
    /// Exige que o usuário esteja autenticado com tenant_id. Super admin sem tenant passa.
    /// Retorna o tenantId efetivo em <paramref name="tenantId"/> quando OK (null se super admin global).
    /// </summary>
    public IActionResult? RequireTenant(out int? tenantId)
    {
        tenantId = _currentUser.TenantId;

        if (_currentUser.IsSuperAdmin)
            return null;

        if (tenantId is null)
        {
            _logger.LogWarning("Acesso negado: usuário sem tenant_id no JWT");
            return new ForbidResult();
        }

        return null;
    }

    /// <summary>
    /// Valida que <paramref name="clinicId"/> (tenant id vindo de query/route) bate com o tenant do JWT.
    /// Super admin pode passar qualquer clinicId.
    /// </summary>
    public IActionResult? EnsureTenantMatches(int clinicId)
    {
        if (clinicId <= 0)
            return new BadRequestObjectResult(new ProblemDetails { Title = "clinicId inválido", Status = 400 });

        if (_currentUser.IsSuperAdmin)
            return null;

        if (_currentUser.TenantId is null)
        {
            _logger.LogWarning("Acesso negado: usuário sem tenant_id no JWT (clinicId={ClinicId})", clinicId);
            return new ForbidResult();
        }

        if (_currentUser.TenantId.Value != clinicId)
        {
            _logger.LogWarning(
                "Cross-tenant negado: user tenant={Tenant} tentou clinicId={Clinic}",
                _currentUser.TenantId.Value, clinicId);
            return new ForbidResult();
        }

        return null;
    }

    /// <summary>
    /// Resolve qual tenantId usar na query de acordo com o usuário e unit selecionada.
    /// <list type="bullet">
    /// <item>Super admin sem unit → tenantId null (sem filtro, vê tudo).</item>
    /// <item>Super admin com unit → tenantId derivado de Unit.ClinicId (pode ser de outro tenant).</item>
    /// <item>Usuário normal sem unit → tenantId do JWT.</item>
    /// <item>Usuário normal com unit → tenantId do JWT + valida que a unit pertence a ele.</item>
    /// </list>
    /// </summary>
    public async Task<(IActionResult? Error, int? TenantId)> ResolveTenantAsync(
        int? unitId, CancellationToken ct = default)
    {
        if (unitId is <= 0)
            return (new BadRequestObjectResult(new ProblemDetails { Title = "unitId inválido", Status = 400 }), null);

        if (_currentUser.IsSuperAdmin)
        {
            if (!unitId.HasValue)
                return (null, null);

            var unitClinicIdSa = await _db.Units
                .AsNoTracking()
                .Where(u => u.Id == unitId.Value)
                .Select(u => (int?)u.ClinicId)
                .FirstOrDefaultAsync(ct);

            if (unitClinicIdSa is null)
                return (new NotFoundObjectResult(new ProblemDetails { Title = "Unidade não encontrada", Status = 404 }), null);

            return (null, unitClinicIdSa);
        }

        if (_currentUser.TenantId is null)
        {
            _logger.LogWarning("Acesso negado: usuário sem tenant_id no JWT");
            return (new ForbidResult(), null);
        }

        if (!unitId.HasValue)
            return (null, _currentUser.TenantId);

        var unitClinicId = await _db.Units
            .AsNoTracking()
            .Where(u => u.Id == unitId.Value)
            .Select(u => (int?)u.ClinicId)
            .FirstOrDefaultAsync(ct);

        if (unitClinicId is null)
            return (new NotFoundObjectResult(new ProblemDetails { Title = "Unidade não encontrada", Status = 404 }), null);

        if (unitClinicId.Value != _currentUser.TenantId.Value)
        {
            _logger.LogWarning(
                "Cross-tenant negado: user tenant={Tenant} tentou unit={Unit} (clinic={Clinic})",
                _currentUser.TenantId.Value, unitId.Value, unitClinicId.Value);
            return (new ForbidResult(), null);
        }

        return (null, _currentUser.TenantId);
    }

    /// <summary>
    /// Valida que <paramref name="unitId"/> pertence ao tenant autenticado.
    /// Super admin pode acessar qualquer unit.
    /// </summary>
    public async Task<IActionResult?> EnsureUnitBelongsToTenantAsync(int unitId, CancellationToken ct = default)
    {
        if (unitId <= 0)
            return new BadRequestObjectResult(new ProblemDetails { Title = "unitId inválido", Status = 400 });

        if (_currentUser.IsSuperAdmin)
            return null;

        if (_currentUser.TenantId is null)
        {
            _logger.LogWarning("Acesso negado: usuário sem tenant_id no JWT (unitId={UnitId})", unitId);
            return new ForbidResult();
        }

        var unitClinicId = await _db.Units
            .AsNoTracking()
            .Where(u => u.Id == unitId)
            .Select(u => (int?)u.ClinicId)
            .FirstOrDefaultAsync(ct);

        if (unitClinicId is null)
            return new NotFoundObjectResult(new ProblemDetails { Title = "Unidade não encontrada", Status = 404 });

        if (unitClinicId.Value != _currentUser.TenantId.Value)
        {
            _logger.LogWarning(
                "Cross-tenant negado: user tenant={Tenant} tentou unit={Unit} (clinic={Clinic})",
                _currentUser.TenantId.Value, unitId, unitClinicId.Value);
            return new ForbidResult();
        }

        return null;
    }
}
