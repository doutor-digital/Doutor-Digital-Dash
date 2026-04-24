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
    /// Lista grupos de contatos duplicados por (TenantId, PhoneNormalized) com paginação.
    /// Mantém o mais antigo de cada grupo.
    /// </summary>
    [HttpGet("duplicates")]
    [ProducesResponseType(typeof(DuplicatesReportDto), 200)]
    public async Task<IActionResult> ListDuplicates(
        [FromQuery] int? tenantId,
        [FromQuery] bool ignoreTenant = false,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        var report = await _duplicateService.FindDuplicatesAsync(tenantId, ignoreTenant, page, pageSize, ct);
        return Ok(report);
    }

    /// <summary>
    /// Apaga contatos duplicados de forma incremental. Cada chamada executa até
    /// `maxBatches` lotes de `batchSize` registros e retorna o progresso.
    /// O cliente repete a chamada até `completed=true`.
    /// Passe dryRun=true (padrão) para só pré-visualizar.
    /// </summary>
    [HttpDelete("duplicates")]
    [ProducesResponseType(typeof(DuplicatesReportDto), 200)]
    [ProducesResponseType(typeof(DuplicatesDeleteProgressDto), 200)]
    public async Task<IActionResult> DeleteDuplicates(
        [FromQuery] bool dryRun = true,
        [FromQuery] int? tenantId = null,
        [FromQuery] bool ignoreTenant = false,
        [FromQuery] int batchSize = DuplicateContactService.DefaultBatchSize,
        [FromQuery] int maxBatches = DuplicateContactService.DefaultMaxBatchesPerCall,
        CancellationToken ct = default)
    {
        if (dryRun)
        {
            var preview = await _duplicateService.FindDuplicatesAsync(tenantId, ignoreTenant, 1, 50, ct);
            return Ok(preview);
        }

        _logger.LogWarning(
            "⚠ DELETE incremental de duplicados por {User} (tenantId={TenantId}, ignoreTenant={IgnoreTenant}, batchSize={BatchSize}, maxBatches={MaxBatches})",
            User.Identity?.Name ?? "anonymous", tenantId, ignoreTenant, batchSize, maxBatches);

        var progress = await _duplicateService.DeleteDuplicatesAsync(
            tenantId, ignoreTenant, batchSize, maxBatches, ct);
        return Ok(progress);
    }
}
