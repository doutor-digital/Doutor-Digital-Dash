using LeadAnalytics.Api.DTOs.Admin;
using LeadAnalytics.Api.Jobs;
using LeadAnalytics.Api.Service;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LeadAnalytics.Api.Controllers;

[ApiController]
[Authorize]
[Route("contacts/admin")]
public class AdminDuplicatesController(
    DuplicateContactService duplicateService,
    IDuplicateDeleteJobQueue jobQueue,
    DuplicateDeleteJobStore jobStore,
    ILogger<AdminDuplicatesController> logger) : ControllerBase
{
    private readonly DuplicateContactService _duplicateService = duplicateService;
    private readonly IDuplicateDeleteJobQueue _jobQueue = jobQueue;
    private readonly DuplicateDeleteJobStore _jobStore = jobStore;
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
    /// Cria um job de exclusão em lote, em background. Retorna o jobId imediatamente.
    /// O cliente consulta o progresso em GET /contacts/admin/duplicates/jobs/{id}.
    /// </summary>
    [HttpPost("duplicates/jobs")]
    [ProducesResponseType(typeof(StartDuplicateDeleteJobResponse), 202)]
    public async Task<IActionResult> StartDeleteJob(
        [FromBody] StartDuplicateDeleteJobRequest body,
        CancellationToken ct)
    {
        var batchSize = Math.Clamp(
            body.BatchSize ?? DuplicateContactService.DefaultBatchSize,
            1,
            DuplicateContactService.MaxBatchSize);

        var job = new DuplicateDeleteJobDto
        {
            Id = Guid.NewGuid().ToString("N"),
            Status = DuplicateDeleteJobStatus.Queued,
            TenantId = body.TenantId,
            IgnoreTenant = body.IgnoreTenant,
            BatchSize = batchSize,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = User.Identity?.Name ?? "anonymous",
        };

        await _jobStore.SaveAsync(job, ct);
        await _jobQueue.EnqueueAsync(
            new DuplicateDeleteJobRequest(job.Id, body.TenantId, body.IgnoreTenant, batchSize),
            ct);

        _logger.LogWarning(
            "📥 Job de delete enfileirado: {JobId} por {User} (tenantId={TenantId}, ignoreTenant={Ignore}, batchSize={BatchSize})",
            job.Id, job.CreatedBy, body.TenantId, body.IgnoreTenant, batchSize);

        return Accepted(
            $"/contacts/admin/duplicates/jobs/{job.Id}",
            new StartDuplicateDeleteJobResponse { JobId = job.Id, Status = job.Status });
    }

    /// <summary>
    /// Consulta o status/progresso de um job de exclusão em lote.
    /// </summary>
    [HttpGet("duplicates/jobs/{jobId}")]
    [ProducesResponseType(typeof(DuplicateDeleteJobDto), 200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetDeleteJob(string jobId, CancellationToken ct)
    {
        var job = await _jobStore.GetAsync(jobId, ct);
        if (job is null) return NotFound();
        return Ok(job);
    }

    /// <summary>
    /// Solicita cancelamento de um job em execução. O worker interrompe ao final do lote corrente.
    /// </summary>
    [HttpDelete("duplicates/jobs/{jobId}")]
    [ProducesResponseType(typeof(DuplicateDeleteJobDto), 200)]
    [ProducesResponseType(404)]
    [ProducesResponseType(409)]
    public async Task<IActionResult> CancelDeleteJob(string jobId, CancellationToken ct)
    {
        var ok = await _jobStore.RequestCancelAsync(jobId, ct);
        var job = await _jobStore.GetAsync(jobId, ct);
        if (job is null) return NotFound();
        if (!ok) return Conflict(job);
        return Ok(job);
    }

    /// <summary>
    /// Dry-run: pré-visualização sem apagar. Mantido para inspeção rápida.
    /// </summary>
    [HttpDelete("duplicates")]
    [ProducesResponseType(typeof(DuplicatesReportDto), 200)]
    public async Task<IActionResult> DryRunDeleteDuplicates(
        [FromQuery] int? tenantId = null,
        [FromQuery] bool ignoreTenant = false,
        CancellationToken ct = default)
    {
        var preview = await _duplicateService.FindDuplicatesAsync(tenantId, ignoreTenant, 1, 50, ct);
        return Ok(preview);
    }
}
