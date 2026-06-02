using LeadAnalytics.Api.DTOs.Admin;
using LeadAnalytics.Api.Service;

namespace LeadAnalytics.Api.Jobs;

public class KommoDedupJobWorker(
    IKommoDedupJobQueue queue,
    IServiceScopeFactory scopeFactory,
    ILogger<KommoDedupJobWorker> logger) : BackgroundService
{
    private readonly IKommoDedupJobQueue _queue = queue;
    private readonly IServiceScopeFactory _scopeFactory = scopeFactory;
    private readonly ILogger<KommoDedupJobWorker> _logger = logger;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("KommoDedupJobWorker iniciado.");

        await foreach (var req in _queue.DequeueAllAsync(stoppingToken))
        {
            using var scope = _scopeFactory.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<KommoDedupService>();
            var store = scope.ServiceProvider.GetRequiredService<KommoDedupJobStore>();

            var job = await store.GetAsync(req.JobId, stoppingToken);
            if (job is null)
            {
                _logger.LogWarning("KommoDedup job {JobId} não encontrado; ignorando.", req.JobId);
                continue;
            }

            try
            {
                await service.RunAsync(job, store, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                job.Status = DuplicateDeleteJobStatus.Failed;
                job.Error = "Serviço reiniciado antes da conclusão.";
                job.FinishedAt = DateTime.UtcNow;
                await store.SaveAsync(job, CancellationToken.None);
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Falha no KommoDedup job {JobId}", req.JobId);
                job.Status = DuplicateDeleteJobStatus.Failed;
                job.Error = ex.Message;
                job.FinishedAt = DateTime.UtcNow;
                await store.SaveAsync(job, CancellationToken.None);
            }
        }
    }
}
