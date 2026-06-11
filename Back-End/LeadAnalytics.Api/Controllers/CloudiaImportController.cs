using LeadAnalytics.Api.DTOs.Imports;
using LeadAnalytics.Api.Service;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LeadAnalytics.Api.Controllers;

/// <summary>
/// Import em massa do CSV "Cadastro Geral" (formato Cloudia legada). Corrige a data
/// real dos leads históricos que vieram do backfill da Kommo com CreatedAt errado
/// (data do 1º sync), populando Lead.OriginalCreatedAt + LeadType. Não toca em Kommo.
/// </summary>
[ApiController]
[Authorize]
[Route("api/imports")]
public class CloudiaImportController(
    CloudiaCsvImportService importService,
    TenantUnitGuard tenantGuard,
    ILogger<CloudiaImportController> logger) : ControllerBase
{
    private readonly CloudiaCsvImportService _importService = importService;
    private readonly TenantUnitGuard _tenantGuard = tenantGuard;
    private readonly ILogger<CloudiaImportController> _logger = logger;

    private const long MaxFileSize = 50 * 1024 * 1024; // 50 MB

    /// <summary>
    /// Sobe um CSV "Cadastro Geral" da Cloudia. Faz match com leads existentes do
    /// nosso DB por nome+data (convenção da SDR) e atualiza OriginalCreatedAt.
    /// Modo dryRun=true só retorna o que faria, sem escrever.
    /// </summary>
    [HttpPost("cloudia-csv")]
    [RequestSizeLimit(MaxFileSize)]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(CloudiaCsvImportResultDto), 200)]
    public async Task<IActionResult> Import(
        IFormFile file,
        [FromForm] int unitId,
        [FromForm] bool dryRun = true,
        [FromForm] bool updateLeadType = true,
        CancellationToken ct = default)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { error = "arquivo ausente ou vazio" });
        if (file.Length > MaxFileSize)
            return BadRequest(new { error = "arquivo maior que 50 MB" });
        if (unitId <= 0)
            return BadRequest(new { error = "unitId inválido" });

        if (await _tenantGuard.EnsureUnitBelongsToTenantAsync(unitId, ct) is { } denied)
            return denied;

        _logger.LogInformation(
            "Cloudia import recebido. UnitId={Unit} DryRun={DryRun} UpdateLeadType={UpdateLeadType} File={File} Size={Size}",
            unitId, dryRun, updateLeadType, file.FileName, file.Length);

        try
        {
            await using var stream = file.OpenReadStream();
            var result = await _importService.ProcessAsync(unitId, stream, dryRun, updateLeadType, ct);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao processar Cloudia CSV import (UnitId={Unit})", unitId);
            return StatusCode(500, new { error = "falha ao processar o arquivo", message = ex.Message });
        }
    }
}
