using LeadAnalytics.Api.DTOs.Admin;
using LeadAnalytics.Api.Jobs;
using LeadAnalytics.Api.Service;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LeadAnalytics.Api.Controllers;

/// <summary>
/// Deduplicação direto na Kommo: acha duplicados lendo a API da Kommo ao vivo e
/// marca a tag "DUPLICADO" lá (não depende do nosso banco). Não apaga — o usuário
/// filtra a tag na Kommo e apaga em massa pela tela deles.
/// </summary>
[ApiController]
[Authorize]
[Route("leads/kommo-dedup")]
public class KommoDedupController(
    IKommoDedupJobQueue jobQueue,
    KommoDedupJobStore jobStore,
    TenantUnitGuard tenantGuard,
    ICurrentUser currentUser,
    ILogger<KommoDedupController> logger) : ControllerBase
{
    private readonly IKommoDedupJobQueue _jobQueue = jobQueue;
    private readonly KommoDedupJobStore _jobStore = jobStore;
    private readonly TenantUnitGuard _tenantGuard = tenantGuard;
    private readonly ICurrentUser _currentUser = currentUser;
    private readonly ILogger<KommoDedupController> _logger = logger;

    [HttpPost("jobs")]
    [ProducesResponseType(typeof(StartKommoDedupResponse), 202)]
    public async Task<IActionResult> StartJob([FromBody] StartKommoDedupRequest body, CancellationToken ct)
    {
        if (body.UnitId <= 0)
            return BadRequest(new ProblemDetails { Title = "unitId é obrigatório", Status = 400 });

        var error = await _tenantGuard.EnsureUnitBelongsToTenantAsync(body.UnitId, ct);
        if (error is not null) return error;

        var mode = string.Equals(body.Mode, "name", StringComparison.OrdinalIgnoreCase) ? "name" : "phone";

        var job = new KommoDedupJobDto
        {
            Id = Guid.NewGuid().ToString("N"),
            Status = DuplicateDeleteJobStatus.Queued,
            UnitId = body.UnitId,
            TenantId = _currentUser.TenantId,
            Mode = mode,
            Apply = body.Apply,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = User.Identity?.Name ?? _currentUser.Email ?? "anonymous",
        };

        await _jobStore.SaveAsync(job, ct);
        await _jobQueue.EnqueueAsync(new KommoDedupJobRequest(job.Id, body.UnitId, mode, body.Apply), ct);

        _logger.LogWarning(
            "📥 KommoDedup job enfileirado: {JobId} por {User} (unit={Unit}, mode={Mode}, apply={Apply})",
            job.Id, job.CreatedBy, body.UnitId, mode, body.Apply);

        return Accepted(
            $"/leads/kommo-dedup/jobs/{job.Id}",
            new StartKommoDedupResponse { JobId = job.Id, Status = job.Status });
    }

    [HttpGet("jobs/{jobId}")]
    [ProducesResponseType(typeof(KommoDedupJobDto), 200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetJob(string jobId, CancellationToken ct)
    {
        var job = await _jobStore.GetAsync(jobId, ct);
        if (job is null) return NotFound();
        return Ok(job);
    }
}
