using LeadAnalytics.Api.DTOs.Admin;
using LeadAnalytics.Api.Service;

namespace LeadAnalytics.Api.Jobs;

public class ContactsBulkDeleteJobWorker(
    IContactsBulkDeleteJobQueue queue,
    IServiceScopeFactory scopeFactory,
    ILogger<ContactsBulkDeleteJobWorker> logger) : BackgroundService
{
    private readonly IContactsBulkDeleteJobQueue _queue = queue;
    private readonly IServiceScopeFactory _scopeFactory = scopeFactory;
    private readonly ILogger<ContactsBulkDeleteJobWorker> _logger = logger;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ContactsBulkDeleteJobWorker iniciado.");

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
                    "Serviço reiniciado antes da conclusão.", CancellationToken.None);
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Falha ao processar bulk delete job {JobId}", req.JobId);
                await MarkTerminalAsync(req.JobId, DuplicateDeleteJobStatus.Failed,
                    ex.Message, CancellationToken.None);
            }
        }
    }

    private async Task ProcessJobAsync(ContactsBulkDeleteJobRequest req, CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var contacts = scope.ServiceProvider.GetRequiredService<ContactService>();
        var store = scope.ServiceProvider.GetRequiredService<ContactsBulkDeleteJobStore>();

        var job = await store.GetAsync(req.JobId, stoppingToken);
        if (job is null) return;

        var selection = await store.GetSelectionAsync(req.JobId, stoppingToken);
        if (selection is null)
        {
            await MarkTerminalAsync(req.JobId, DuplicateDeleteJobStatus.Failed,
                "Seleção do job não encontrada no store.", stoppingToken);
            return;
        }

        var expectedTotal = await contacts.CountBulkDeleteCandidatesAsync(
            req.TenantId, selection, stoppingToken);

        job.Status = DuplicateDeleteJobStatus.Running;
        job.StartedAt = DateTime.UtcNow;
        job.ContactsToDeleteTotal = expectedTotal;
        await store.SaveAsync(job, stoppingToken);

        _logger.LogInformation(
            "▶ Bulk delete {JobId} — {Total} contato(s) alvo (tenant={Tenant}, mode={Mode}, batch={Batch})",
            job.Id, expectedTotal, req.TenantId, selection.Mode, req.BatchSize);

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
                _logger.LogWarning("🛑 Bulk delete {JobId} cancelado após {Deleted}/{Expected}",
                    job.Id, live.ContactsDeleted, live.ContactsToDeleteTotal);
                return;
            }

            int affected;
            try
            {
                affected = await contacts.DeleteBulkBatchAsync(
                    req.TenantId, selection, req.BatchSize, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro no lote do bulk delete {JobId}", job.Id);
                live.Status = DuplicateDeleteJobStatus.Failed;
                live.Error = ex.Message;
                live.FinishedAt = DateTime.UtcNow;
                await store.SaveAsync(live, stoppingToken);
                return;
            }

            live.ContactsDeleted += affected;
            live.BatchesExecuted += 1;
            await store.SaveAsync(live, stoppingToken);

            _logger.LogInformation(
                "🗑 Bulk delete {JobId} lote {Batch}: {Affected} linha(s) (acum={Acc}/{Expected})",
                job.Id, live.BatchesExecuted, affected, live.ContactsDeleted, live.ContactsToDeleteTotal);

            // Modo IDs: o count de candidatos não diminui depois do delete (IDs são fixos).
            // Modo Filter: após deletar, novos candidatos podem aparecer (ex: webhook).
            // Em ambos os casos, `affected < batchSize` é sinal de fim.
            if (affected < req.BatchSize)
            {
                live.Status = DuplicateDeleteJobStatus.Completed;
                live.FinishedAt = DateTime.UtcNow;
                await store.SaveAsync(live, stoppingToken);
                _logger.LogInformation("✅ Bulk delete {JobId} concluído: {Deleted} em {Batches} lote(s)",
                    job.Id, live.ContactsDeleted, live.BatchesExecuted);
                return;
            }

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
            var store = scope.ServiceProvider.GetRequiredService<ContactsBulkDeleteJobStore>();
            var job = await store.GetAsync(jobId, ct);
            if (job is null) return;
            job.Status = status;
            job.Error = error;
            job.FinishedAt = DateTime.UtcNow;
            await store.SaveAsync(job, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao marcar bulk delete {JobId} como terminal", jobId);
        }
    }
}
