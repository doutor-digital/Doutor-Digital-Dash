using LeadAnalytics.Api.DTOs.Admin;
using LeadAnalytics.Api.Jobs;
using LeadAnalytics.Api.Service;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LeadAnalytics.Api.Controllers;

/// <summary>
/// Deduplicação de LEADS (por telefone), mantendo o lead mais avançado de cada grupo.
/// Exclusão em background (job). A API da Kommo não apaga leads, então (opcional) os
/// duplicados são marcados com a tag "DUPLICADO" na Kommo e apagados do nosso banco.
///
/// Tenant é resolvido pelo JWT: usuário comum só mexe no próprio tenant; super admin
/// pode informar tenantId/ignoreTenant.
/// </summary>
[ApiController]
[Authorize]
[Route("leads/admin")]
public class AdminLeadDuplicatesController(
    DuplicateLeadService dedup,
    ILeadDuplicateDeleteJobQueue jobQueue,
    LeadDuplicateDeleteJobStore jobStore,
    ICurrentUser currentUser,
    ILogger<AdminLeadDuplicatesController> logger) : ControllerBase
{
    private readonly DuplicateLeadService _dedup = dedup;
    private readonly ILeadDuplicateDeleteJobQueue _jobQueue = jobQueue;
    private readonly LeadDuplicateDeleteJobStore _jobStore = jobStore;
    private readonly ICurrentUser _currentUser = currentUser;
    private readonly ILogger<AdminLeadDuplicatesController> _logger = logger;

    /// <summary>Resolve (tenantId, ignoreTenant) efetivos a partir do JWT.</summary>
    private (int? TenantId, bool IgnoreTenant, IActionResult? Error) ResolveScope(int? tenantId, bool ignoreTenant)
    {
        if (_currentUser.IsSuperAdmin)
            return (tenantId, ignoreTenant, null);

        if (_currentUser.TenantId is null)
            return (null, false, Forbid());

        // Usuário comum: trava no próprio tenant.
        return (_currentUser.TenantId, false, null);
    }

    [HttpGet("duplicates")]
    [ProducesResponseType(typeof(LeadDuplicatesReportDto), 200)]
    public async Task<IActionResult> ListDuplicates(
        [FromQuery] int? tenantId,
        [FromQuery] bool ignoreTenant = false,
        [FromQuery] string mode = "phone",
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        var (t, ig, error) = ResolveScope(tenantId, ignoreTenant);
        if (error is not null) return error;

        var report = await _dedup.FindDuplicatesAsync(t, ig, mode, page, pageSize, ct);
        return Ok(report);
    }

    [HttpPost("duplicates/jobs")]
    [ProducesResponseType(typeof(StartLeadDuplicateDeleteJobResponse), 202)]
    public async Task<IActionResult> StartDeleteJob(
        [FromBody] StartLeadDuplicateDeleteJobRequest body,
        CancellationToken ct)
    {
        var (t, ig, error) = ResolveScope(body.TenantId, body.IgnoreTenant);
        if (error is not null) return error;

        var batchSize = Math.Clamp(
            body.BatchSize ?? DuplicateLeadService.DefaultBatchSize, 1, DuplicateLeadService.MaxBatchSize);

        var mode = string.Equals(body.Mode, DuplicateLeadService.ModeName, StringComparison.OrdinalIgnoreCase)
            ? DuplicateLeadService.ModeName
            : DuplicateLeadService.ModePhone;

        var job = new LeadDuplicateDeleteJobDto
        {
            Id = Guid.NewGuid().ToString("N"),
            Status = DuplicateDeleteJobStatus.Queued,
            TenantId = t,
            IgnoreTenant = ig,
            BatchSize = batchSize,
            TagInKommo = body.TagInKommo,
            Mode = mode,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = User.Identity?.Name ?? _currentUser.Email ?? "anonymous",
        };

        await _jobStore.SaveAsync(job, ct);
        await _jobQueue.EnqueueAsync(
            new LeadDuplicateDeleteJobRequest(job.Id, t, ig, batchSize, body.TagInKommo, mode), ct);

        _logger.LogWarning(
            "📥 Job de delete de LEADS enfileirado: {JobId} por {User} (tenant={Tenant}, tagKommo={Tag}, batch={Batch})",
            job.Id, job.CreatedBy, t, body.TagInKommo, batchSize);

        return Accepted(
            $"/leads/admin/duplicates/jobs/{job.Id}",
            new StartLeadDuplicateDeleteJobResponse { JobId = job.Id, Status = job.Status });
    }

    [HttpGet("duplicates/jobs/{jobId}")]
    [ProducesResponseType(typeof(LeadDuplicateDeleteJobDto), 200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetDeleteJob(string jobId, CancellationToken ct)
    {
        var job = await _jobStore.GetAsync(jobId, ct);
        if (job is null) return NotFound();
        return Ok(job);
    }

    [HttpDelete("duplicates/jobs/{jobId}")]
    [ProducesResponseType(typeof(LeadDuplicateDeleteJobDto), 200)]
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
}
