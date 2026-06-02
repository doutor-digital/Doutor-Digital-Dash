using LeadAnalytics.Api.DTOs.Admin;
using LeadAnalytics.Api.Service;

namespace LeadAnalytics.Api.Jobs;

public class LeadDuplicateDeleteJobWorker(
    ILeadDuplicateDeleteJobQueue queue,
    IServiceScopeFactory scopeFactory,
    ILogger<LeadDuplicateDeleteJobWorker> logger) : BackgroundService
{
    private readonly ILeadDuplicateDeleteJobQueue _queue = queue;
    private readonly IServiceScopeFactory _scopeFactory = scopeFactory;
    private readonly ILogger<LeadDuplicateDeleteJobWorker> _logger = logger;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("LeadDuplicateDeleteJobWorker iniciado.");

        await foreach (var req in _queue.DequeueAllAsync(stoppingToken))
        {
            try
            {
                await ProcessJobAsync(req, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                _logger.LogWarning("Worker encerrando. Job {JobId} interrompido.", req.JobId);
                await MarkTerminalAsync(req.JobId, DuplicateDeleteJobStatus.Failed,
                    "Serviço reiniciado antes da conclusão.", stoppingToken);
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Falha ao processar job de leads {JobId}", req.JobId);
                await MarkTerminalAsync(req.JobId, DuplicateDeleteJobStatus.Failed, ex.Message, CancellationToken.None);
            }
        }
    }

    private async Task ProcessJobAsync(LeadDuplicateDeleteJobRequest req, CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var dedup = scope.ServiceProvider.GetRequiredService<DuplicateLeadService>();
        var store = scope.ServiceProvider.GetRequiredService<LeadDuplicateDeleteJobStore>();

        var job = await store.GetAsync(req.JobId, stoppingToken);
        if (job is null)
        {
            _logger.LogWarning("Job {JobId} não encontrado; ignorando.", req.JobId);
            return;
        }

        var (groupsFound, expectedTotal) = await dedup.GetDeleteEstimateAsync(
            req.TenantId, req.IgnoreTenant, stoppingToken);

        job.Status = DuplicateDeleteJobStatus.Running;
        job.StartedAt = DateTime.UtcNow;
        job.LeadsToDeleteTotal = expectedTotal;
        job.GroupsFound = groupsFound;
        await store.SaveAsync(job, stoppingToken);

        _logger.LogWarning(
            "▶ Job leads {JobId} iniciado — {Expected} a apagar em {Groups} grupo(s) (tenant={Tenant}, tagKommo={Tag})",
            job.Id, expectedTotal, groupsFound, req.TenantId, req.TagInKommo);

        if (expectedTotal == 0)
        {
            job.Status = DuplicateDeleteJobStatus.Completed;
            job.FinishedAt = DateTime.UtcNow;
            await store.SaveAsync(job, stoppingToken);
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var live = await store.GetAsync(job.Id, stoppingToken);
            if (live is null) return;

            if (live.Status == DuplicateDeleteJobStatus.Cancelling)
            {
                live.Status = DuplicateDeleteJobStatus.Cancelled;
                live.FinishedAt = DateTime.UtcNow;
                await store.SaveAsync(live, stoppingToken);
                _logger.LogWarning("🛑 Job leads {JobId} cancelado após {Deleted}/{Expected}",
                    job.Id, live.LeadsDeleted, live.LeadsToDeleteTotal);
                return;
            }

            DuplicateLeadService.BatchResult res;
            try
            {
                res = await dedup.DeleteOneBatchAsync(
                    req.TenantId, req.IgnoreTenant, req.BatchSize, req.TagInKommo, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro no lote do job leads {JobId}. Abortando.", job.Id);
                live.Status = DuplicateDeleteJobStatus.Failed;
                live.Error = ex.Message;
                live.FinishedAt = DateTime.UtcNow;
                await store.SaveAsync(live, stoppingToken);
                return;
            }

            live.LeadsDeleted += res.Deleted;
            live.TaggedInKommo += res.Tagged;
            live.TagConfirmed += res.TagConfirmed;
            live.TagFailures += res.TagFailed;
            live.TagSkipped += res.TagSkipped;
            live.BatchesExecuted += 1;
            await store.SaveAsync(live, stoppingToken);

            _logger.LogInformation(
                "🗑 Job leads {JobId} lote {Batch}: {Affected} apagado(s) (acum={Acc}/{Expected})",
                job.Id, live.BatchesExecuted, res.Deleted, live.LeadsDeleted, live.LeadsToDeleteTotal);

            if (res.Deleted < req.BatchSize)
            {
                live.Status = DuplicateDeleteJobStatus.Completed;
                live.FinishedAt = DateTime.UtcNow;
                await store.SaveAsync(live, stoppingToken);
                _logger.LogWarning("✅ Job leads {JobId} concluído: {Deleted} apagado(s), {Tagged} tagueado(s)",
                    job.Id, live.LeadsDeleted, live.TaggedInKommo);
                return;
            }

            await Task.Delay(50, stoppingToken);
        }
    }

    private async Task MarkTerminalAsync(
        string jobId, DuplicateDeleteJobStatus status, string? error, CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<LeadDuplicateDeleteJobStore>();
            var job = await store.GetAsync(jobId, ct);
            if (job is null) return;
            job.Status = status;
            job.Error = error;
            job.FinishedAt = DateTime.UtcNow;
            await store.SaveAsync(job, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao marcar job leads {JobId} como terminal ({Status})", jobId, status);
        }
    }
}
