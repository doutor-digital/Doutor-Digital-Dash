using LeadAnalytics.Api.DTOs.Admin;
using LeadAnalytics.Api.Service;

namespace LeadAnalytics.Api.Jobs;

public class DuplicateDeleteJobWorker(
    IDuplicateDeleteJobQueue queue,
    IServiceScopeFactory scopeFactory,
    ILogger<DuplicateDeleteJobWorker> logger) : BackgroundService
{
    private readonly IDuplicateDeleteJobQueue _queue = queue;
    private readonly IServiceScopeFactory _scopeFactory = scopeFactory;
    private readonly ILogger<DuplicateDeleteJobWorker> _logger = logger;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("DuplicateDeleteJobWorker iniciado.");

        await foreach (var req in _queue.DequeueAllAsync(stoppingToken))
        {
            try
            {
                await ProcessJobAsync(req, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                _logger.LogWarning("Worker encerrando. Job {JobId} será interrompido.", req.JobId);
                await MarkTerminalAsync(req.JobId, DuplicateDeleteJobStatus.Failed,
                    "Serviço reiniciado antes da conclusão.", stoppingToken);
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Falha ao processar job {JobId}", req.JobId);
                await MarkTerminalAsync(req.JobId, DuplicateDeleteJobStatus.Failed,
                    ex.Message, CancellationToken.None);
            }
        }
    }

    private async Task ProcessJobAsync(DuplicateDeleteJobRequest req, CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var duplicates = scope.ServiceProvider.GetRequiredService<DuplicateContactService>();
        var store = scope.ServiceProvider.GetRequiredService<DuplicateDeleteJobStore>();

        var job = await store.GetAsync(req.JobId, stoppingToken);
        if (job is null)
        {
            _logger.LogWarning("Job {JobId} não encontrado no store; ignorando.", req.JobId);
            return;
        }

        var (groupsFound, expectedTotal) = await duplicates.GetDeleteEstimateAsync(
            req.TenantId, req.IgnoreTenant, stoppingToken);

        job.Status = DuplicateDeleteJobStatus.Running;
        job.StartedAt = DateTime.UtcNow;
        job.ContactsToDeleteTotal = expectedTotal;
        job.GroupsFound = groupsFound;
        await store.SaveAsync(job, stoppingToken);

        _logger.LogInformation(
            "▶ Job {JobId} iniciado — {Expected} a apagar em {Groups} grupo(s) (tenant={Tenant}, ignoreTenant={Ignore}, batchSize={Batch})",
            job.Id, expectedTotal, groupsFound, req.TenantId, req.IgnoreTenant, req.BatchSize);

        if (expectedTotal == 0)
        {
            job.Status = DuplicateDeleteJobStatus.Completed;
            job.FinishedAt = DateTime.UtcNow;
            await store.SaveAsync(job, stoppingToken);
            await duplicates.InvalidateReportCacheAsync(req.TenantId, stoppingToken);
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var live = await store.GetAsync(job.Id, stoppingToken);
            if (live is null)
            {
                _logger.LogWarning("Job {JobId} sumiu do store; interrompendo.", job.Id);
                return;
            }

            if (live.Status == DuplicateDeleteJobStatus.Cancelling)
            {
                live.Status = DuplicateDeleteJobStatus.Cancelled;
                live.FinishedAt = DateTime.UtcNow;
                await store.SaveAsync(live, stoppingToken);
                await duplicates.InvalidateReportCacheAsync(req.TenantId, stoppingToken);
                _logger.LogWarning("🛑 Job {JobId} cancelado pelo usuário após {Deleted}/{Expected}",
                    job.Id, live.ContactsDeleted, live.ContactsToDeleteTotal);
                return;
            }

            int affected;
            try
            {
                affected = await duplicates.DeleteOneBatchAsync(
                    req.TenantId, req.IgnoreTenant, req.BatchSize, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro no lote do job {JobId}. Abortando.", job.Id);
                live.Status = DuplicateDeleteJobStatus.Failed;
                live.Error = ex.Message;
                live.FinishedAt = DateTime.UtcNow;
                await store.SaveAsync(live, stoppingToken);
                await duplicates.InvalidateReportCacheAsync(req.TenantId, stoppingToken);
                return;
            }

            live.ContactsDeleted += affected;
            live.BatchesExecuted += 1;
            await store.SaveAsync(live, stoppingToken);

            _logger.LogInformation(
                "🗑 Job {JobId} lote {Batch}: {Affected} linha(s) (acum={Acc}/{Expected})",
                job.Id, live.BatchesExecuted, affected, live.ContactsDeleted, live.ContactsToDeleteTotal);

            if (affected < req.BatchSize)
            {
                live.Status = DuplicateDeleteJobStatus.Completed;
                live.FinishedAt = DateTime.UtcNow;
                await store.SaveAsync(live, stoppingToken);
                await duplicates.InvalidateReportCacheAsync(req.TenantId, stoppingToken);
                _logger.LogInformation("✅ Job {JobId} concluído: {Deleted} em {Batches} lote(s)",
                    job.Id, live.ContactsDeleted, live.BatchesExecuted);
                return;
            }

            // Entrega fatia de CPU/DB para outras queries entre lotes.
            await Task.Delay(50, stoppingToken);
        }
    }

    private async Task MarkTerminalAsync(
        string jobId,
        DuplicateDeleteJobStatus status,
        string? error,
        CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<DuplicateDeleteJobStore>();
            var job = await store.GetAsync(jobId, ct);
            if (job is null) return;
            job.Status = status;
            job.Error = error;
            job.FinishedAt = DateTime.UtcNow;
            await store.SaveAsync(job, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao marcar job {JobId} como terminal ({Status})", jobId, status);
        }
    }
}
