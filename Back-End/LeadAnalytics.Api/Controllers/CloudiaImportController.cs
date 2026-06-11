using LeadAnalytics.Api.DTOs.Imports;
using LeadAnalytics.Api.Service;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LeadAnalytics.Api.Controllers;

/// <summary>
/// Import em massa do CSV "Cadastro Geral" (formato Cloudia legada). Corrige a data
/// real dos leads históricos que vieram do backfill da Kommo com CreatedAt errado
/// (data do 1º sync), populando Lead.OriginalCreatedAt + LeadType. Não toca em Kommo.
/// Cada apply gera um batch com snapshot, permitindo REVERT.
/// </summary>
[ApiController]
[Authorize]
[Route("api/imports")]
public class CloudiaImportController(
    CloudiaCsvImportService importService,
    CloudiaKommoPatchService kommoPatchService,
    TenantUnitGuard tenantGuard,
    ICurrentUser currentUser,
    ILogger<CloudiaImportController> logger) : ControllerBase
{
    private readonly CloudiaCsvImportService _importService = importService;
    private readonly CloudiaKommoPatchService _kommoPatchService = kommoPatchService;
    private readonly TenantUnitGuard _tenantGuard = tenantGuard;
    private readonly ICurrentUser _currentUser = currentUser;
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

        var tenantId = _currentUser.TenantId ?? 0;

        _logger.LogInformation(
            "Cloudia import recebido. UnitId={Unit} TenantId={Tenant} User={UserId} DryRun={DryRun} UpdateLeadType={UpdateLeadType} File={File} Size={Size}",
            unitId, tenantId, _currentUser.UserId, dryRun, updateLeadType, file.FileName, file.Length);

        try
        {
            await using var stream = file.OpenReadStream();
            var result = await _importService.ProcessAsync(
                unitId, tenantId, stream, file.FileName,
                _currentUser.UserId, dryRun, updateLeadType, ct);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao processar Cloudia CSV import (UnitId={Unit})", unitId);
            return StatusCode(500, new { error = "falha ao processar o arquivo", message = ex.Message });
        }
    }

    /// <summary>Lista os 50 imports mais recentes de uma unidade (pra menu de revert).</summary>
    [HttpGet("cloudia-csv/batches")]
    [ProducesResponseType(typeof(List<CloudiaImportBatchDto>), 200)]
    public async Task<IActionResult> ListBatches(
        [FromQuery] int unitId,
        CancellationToken ct = default)
    {
        if (unitId <= 0)
            return BadRequest(new { error = "unitId inválido" });
        if (await _tenantGuard.EnsureUnitBelongsToTenantAsync(unitId, ct) is { } denied)
            return denied;

        var batches = await _importService.ListBatchesAsync(unitId, ct);
        return Ok(batches);
    }

    /// <summary>
    /// Reverte um batch: restaura Lead.OriginalCreatedAt + LeadType pros valores
    /// que tinham ANTES desse batch ser aplicado. Idempotente — chama 2x não desfaz nada.
    /// </summary>
    [HttpPost("cloudia-csv/batches/{batchId:int}/revert")]
    [ProducesResponseType(typeof(CloudiaRevertResultDto), 200)]
    [ProducesResponseType(404)]
    [ProducesResponseType(409)]
    public async Task<IActionResult> RevertBatch(int batchId, CancellationToken ct = default)
    {
        try
        {
            var r = await _importService.RevertBatchAsync(batchId, _currentUser.UserId, ct);
            if (r is null) return NotFound(new { error = "batch não encontrado" });
            return Ok(r);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao reverter batch {BatchId}", batchId);
            return StatusCode(500, new { error = "falha ao reverter", message = ex.Message });
        }
    }

    // ═════════════════════════════════════════════════════════════════════
    //  KOMMO PATCH BACKGROUND JOB
    //  Pega um batch de import já aplicado e PATCHa os custom fields no
    //  Kommo (Tipo de lead, Data de criação lead, Origem, Interação, Motivo,
    //  Tipo de resgaste, Sexo, Qualificação, Observações de consulta). Roda
    //  em background — front faz polling do status.
    // ═════════════════════════════════════════════════════════════════════

    public record StartKommoPatchRequest(List<string> Fields);

    [HttpPost("cloudia-csv/batches/{batchId:int}/kommo-patch")]
    [ProducesResponseType(200)]
    [ProducesResponseType(400)]
    public async Task<IActionResult> StartKommoPatch(
        int batchId, [FromBody] StartKommoPatchRequest body, CancellationToken ct)
    {
        if (body?.Fields is null || body.Fields.Count == 0)
            return BadRequest(new { error = "Selecione pelo menos 1 campo." });
        try
        {
            var job = await _kommoPatchService.StartJobAsync(batchId, body.Fields, _currentUser.UserId, ct);
            return Ok(new { job_id = job.Id, status = job.Status, total = job.Total });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao iniciar Kommo PATCH job pro batch {BatchId}", batchId);
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpGet("cloudia-csv/kommo-jobs/{jobId}")]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetKommoJob(string jobId, CancellationToken ct)
    {
        var j = await _kommoPatchService.GetJobAsync(jobId, ct);
        if (j is null) return NotFound();
        return Ok(new
        {
            id = j.Id,
            batch_id = j.BatchId,
            unit_id = j.UnitId,
            status = j.Status,
            total = j.Total,
            processed = j.Processed,
            succeeded = j.Succeeded,
            failed = j.Failed,
            error_message = j.ErrorMessage,
            created_at = j.CreatedAt,
            started_at = j.StartedAt,
            finished_at = j.FinishedAt,
            cancel_requested = j.CancelRequested,
        });
    }

    [HttpGet("cloudia-csv/kommo-jobs")]
    [ProducesResponseType(200)]
    public async Task<IActionResult> ListKommoJobs([FromQuery] int unitId, CancellationToken ct)
    {
        if (unitId <= 0) return BadRequest(new { error = "unitId inválido" });
        if (await _tenantGuard.EnsureUnitBelongsToTenantAsync(unitId, ct) is { } denied) return denied;
        var jobs = await _kommoPatchService.ListJobsAsync(unitId, ct);
        return Ok(jobs.Select(j => new
        {
            id = j.Id,
            batch_id = j.BatchId,
            unit_id = j.UnitId,
            status = j.Status,
            total = j.Total,
            processed = j.Processed,
            succeeded = j.Succeeded,
            failed = j.Failed,
            error_message = j.ErrorMessage,
            created_at = j.CreatedAt,
            started_at = j.StartedAt,
            finished_at = j.FinishedAt,
        }));
    }

    [HttpPost("cloudia-csv/kommo-jobs/{jobId}/cancel")]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> CancelKommoJob(string jobId, CancellationToken ct)
    {
        var ok = await _kommoPatchService.RequestCancelAsync(jobId, ct);
        if (!ok) return NotFound(new { error = "job não encontrado ou já finalizado" });
        return Ok(new { ok = true });
    }
}
