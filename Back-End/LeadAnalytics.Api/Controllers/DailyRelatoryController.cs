using LeadAnalytics.Api.Service;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LeadAnalytics.Api.Controllers;

[ApiController]
// Relatório com nomes de lead: exige login e só o tenant dono (ver EnsureTenantMatches).
[Authorize]
[Route("daily-relatory")]
public class DailyRelatoryController(
    DailyRelatoryService dailyRelatoryService,
    TenantUnitGuard tenantGuard) : ControllerBase
{
    private readonly DailyRelatoryService _dailyRelatoryService = dailyRelatoryService;
    private readonly TenantUnitGuard _tenantGuard = tenantGuard;

    [HttpGet("generate")]
    public async Task<IActionResult> Generate([FromQuery] int tenantId, [FromQuery] DateTime date)
    {
        if(!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        // O "tenantId" daqui é o id da CLÍNICA (ClinicId), igual ao clinicId das outras rotas.
        if (_tenantGuard.EnsureTenantMatches(tenantId) is { } negado) return negado;

        var relatorio = await _dailyRelatoryService.GenerateDailyRelatory(tenantId, date);
        return Ok(relatorio);
    }
}
