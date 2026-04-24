using LeadAnalytics.Api.DTOs.Admin;
using LeadAnalytics.Api.Jobs;
using LeadAnalytics.Api.Service;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LeadAnalytics.Api.Controllers;

[ApiController]
[Authorize]
[Route("contacts/bulk-delete")]
public class ContactsBulkDeleteController(
    ContactService contactService,
    IContactsBulkDeleteJobQueue jobQueue,
    ContactsBulkDeleteJobStore jobStore,
    ILogger<ContactsBulkDeleteController> logger) : ControllerBase
{
    private readonly ContactService _contactService = contactService;
    private readonly IContactsBulkDeleteJobQueue _jobQueue = jobQueue;
    private readonly ContactsBulkDeleteJobStore _jobStore = jobStore;
    private readonly ILogger<ContactsBulkDeleteController> _logger = logger;

    /// <summary>
    /// Cria um job de exclusão em lote de contatos.
    /// Aceita dois modos: por IDs explícitos ou por filtros (seleciona tudo que bate).
    /// Retorna 202 com jobId + estimativa. Cliente faz polling em GET /jobs/{id}.
    /// </summary>
    [HttpPost("jobs")]
    [ProducesResponseType(typeof(StartContactsBulkDeleteResponse), 202)]
    [ProducesResponseType(typeof(ProblemDetails), 400)]
    public async Task<IActionResult> StartJob(
        [FromBody] StartContactsBulkDeleteRequest body,
        CancellationToken ct)
    {
        if (body.TenantId <= 0)
            return Problem(title: "tenantId inválido", statusCode: 400);

        var selection = body.Selection ?? new ContactsBulkDeleteSelection();

        if (selection.Mode == ContactsBulkDeleteMode.Ids)
        {
            var count = selection.Ids?.Count ?? 0;
            if (count == 0)
                return Problem(title: "Nenhum ID informado", statusCode: 400);
            if (count > ContactService.BulkDeleteMaxIds)
                return Problem(
                    title: $"Máximo de {ContactService.BulkDeleteMaxIds} IDs por job. Use modo filter para volumes maiores.",
                    statusCode: 400);
        }
        else
        {
            if (selection.Filters is null)
                return Problem(title: "Modo filter exige 'filters' no body", statusCode: 400);
        }

        int estimate;
        try
        {
            estimate = await _contactService.CountBulkDeleteCandidatesAsync(body.TenantId, selection, ct);
        }
        catch (ArgumentException ex)
        {
            return Problem(title: ex.Message, statusCode: 400);
        }

        var batchSize = Math.Clamp(
            body.BatchSize ?? ContactService.BulkDeleteDefaultBatchSize,
            1,
            ContactService.BulkDeleteMaxBatchSize);

        var job = new ContactsBulkDeleteJobDto
        {
            Id = Guid.NewGuid().ToString("N"),
            Status = DuplicateDeleteJobStatus.Queued,
            TenantId = body.TenantId,
            Mode = selection.Mode,
            BatchSize = batchSize,
            ContactsToDeleteTotal = estimate,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = User.Identity?.Name ?? "anonymous",
        };

        await _jobStore.SaveAsync(job, ct);
        await _jobStore.SaveSelectionAsync(job.Id, selection, ct);
        await _jobQueue.EnqueueAsync(
            new ContactsBulkDeleteJobRequest(job.Id, body.TenantId, batchSize),
            ct);

        _logger.LogWarning(
            "📥 Bulk delete enfileirado: {JobId} por {User} (tenant={Tenant}, mode={Mode}, ~{Est} contato(s), batch={Batch})",
            job.Id, job.CreatedBy, body.TenantId, selection.Mode, estimate, batchSize);

        return Accepted(
            $"/contacts/bulk-delete/jobs/{job.Id}",
            new StartContactsBulkDeleteResponse
            {
                JobId = job.Id,
                Status = job.Status,
                EstimatedTotal = estimate,
            });
    }

    /// <summary>
    /// Consulta status/progresso de um bulk delete job.
    /// </summary>
    [HttpGet("jobs/{jobId}")]
    [ProducesResponseType(typeof(ContactsBulkDeleteJobDto), 200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetJob(string jobId, CancellationToken ct)
    {
        var job = await _jobStore.GetAsync(jobId, ct);
        if (job is null) return NotFound();
        return Ok(job);
    }

    /// <summary>
    /// Solicita cancelamento. Worker interrompe ao final do lote corrente.
    /// </summary>
    [HttpDelete("jobs/{jobId}")]
    [ProducesResponseType(typeof(ContactsBulkDeleteJobDto), 200)]
    [ProducesResponseType(404)]
    [ProducesResponseType(409)]
    public async Task<IActionResult> CancelJob(string jobId, CancellationToken ct)
    {
        var ok = await _jobStore.RequestCancelAsync(jobId, ct);
        var job = await _jobStore.GetAsync(jobId, ct);
        if (job is null) return NotFound();
        if (!ok) return Conflict(job);
        return Ok(job);
    }
}
