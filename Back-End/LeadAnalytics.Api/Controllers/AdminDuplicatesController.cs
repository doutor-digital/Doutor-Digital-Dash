using LeadAnalytics.Api.DTOs.Admin;
using LeadAnalytics.Api.Service;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LeadAnalytics.Api.Controllers;

[ApiController]
[Authorize]
[Route("contacts/admin")]
public class AdminDuplicatesController(
    DuplicateContactService duplicateService,
    ILogger<AdminDuplicatesController> logger) : ControllerBase
{
    private readonly DuplicateContactService _duplicateService = duplicateService;
    private readonly ILogger<AdminDuplicatesController> _logger = logger;

    /// <summary>
    /// Lista grupos de contatos duplicados por (TenantId, PhoneNormalized).
    /// Mantém o mais antigo de cada grupo.
    /// </summary>
    [HttpGet("duplicates")]
    [ProducesResponseType(typeof(DuplicatesReportDto), 200)]
    public async Task<IActionResult> ListDuplicates(
        [FromQuery] int? tenantId,
        [FromQuery] bool ignoreTenant = false,
        CancellationToken ct = default)
    {
        var report = await _duplicateService.FindDuplicatesAsync(tenantId, ignoreTenant, ct);
        return Ok(report);
    }

    /// <summary>
    /// Apaga contatos duplicados (mantém o mais antigo de cada grupo).
    /// Por padrão roda em dry-run e só retorna o relatório. Passe dryRun=false para efetivar.
    /// </summary>
    [HttpDelete("duplicates")]
    [ProducesResponseType(typeof(DuplicatesReportDto), 200)]
    [ProducesResponseType(typeof(DuplicatesDeleteSummaryDto), 200)]
    public async Task<IActionResult> DeleteDuplicates(
        [FromQuery] bool dryRun = true,
        [FromQuery] int? tenantId = null,
        [FromQuery] bool ignoreTenant = false,
        CancellationToken ct = default)
    {
        if (dryRun)
        {
            var preview = await _duplicateService.FindDuplicatesAsync(tenantId, ignoreTenant, ct);
            return Ok(preview);
        }

        _logger.LogWarning(
            "⚠ Solicitação de DELETE real de contatos duplicados por {User} (tenantId={TenantId}, ignoreTenant={IgnoreTenant})",
            User.Identity?.Name ?? "anonymous", tenantId, ignoreTenant);

        var summary = await _duplicateService.DeleteDuplicatesAsync(tenantId, ignoreTenant, ct);
        return Ok(summary);
    }
}
