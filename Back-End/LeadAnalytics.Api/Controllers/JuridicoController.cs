using LeadAnalytics.Api.Service;
using LeadAnalytics.Api.Service.Juridico;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LeadAnalytics.Api.Controllers;

/// <summary>
/// Endpoints do dashboard do segmento jurídico (ex.: Advocacia Magalhães). Mesma estrutura
/// de autorização por tenant/unidade dos demais dashboards — muda só o conjunto de métricas.
/// </summary>
[ApiController]
[Authorize]
[Route("api/juridico")]
public class JuridicoController(
    JuridicoDashboardService service,
    TenantUnitGuard tenantGuard) : ControllerBase
{
    private const int MaxPeriodDays = 366;

    /// <summary>Dashboard jurídico completo (7 grupos de métricas) para o tenant/unidade no período.</summary>
    [HttpGet("dashboard")]
    public async Task<IActionResult> Dashboard(
        [FromQuery] int clinicId,
        [FromQuery] int? unitId,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        CancellationToken ct = default)
    {
        if (tenantGuard.EnsureTenantMatches(clinicId) is { } denied) return denied;
        if (unitId.HasValue && await tenantGuard.EnsureUnitBelongsToTenantAsync(unitId.Value, ct) is { } guard)
            return guard;

        // Período padrão: últimos 30 dias.
        var toResolved = to ?? DateTime.UtcNow;
        var fromResolved = from ?? toResolved.AddDays(-30);

        if (fromResolved > toResolved)
            return BadRequest(new ProblemDetails { Title = "from deve ser menor ou igual a to", Status = 400 });
        if ((toResolved - fromResolved).TotalDays > MaxPeriodDays)
            return BadRequest(new ProblemDetails { Title = $"Intervalo máximo é {MaxPeriodDays} dias", Status = 400 });

        var dto = await service.BuildAsync(clinicId, unitId, fromResolved, toResolved, ct);
        return Ok(dto);
    }
}
