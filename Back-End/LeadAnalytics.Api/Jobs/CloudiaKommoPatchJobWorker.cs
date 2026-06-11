using LeadAnalytics.Api.Service;

namespace LeadAnalytics.Api.Jobs;

public class CloudiaKommoPatchJobWorker(
    ICloudiaKommoPatchJobQueue queue,
    IServiceScopeFactory scopeFactory,
    ILogger<CloudiaKommoPatchJobWorker> logger) : BackgroundService
{
    private readonly ICloudiaKommoPatchJobQueue _queue = queue;
    private readonly IServiceScopeFactory _scopeFactory = scopeFactory;
    private readonly ILogger<CloudiaKommoPatchJobWorker> _logger = logger;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("CloudiaKommoPatchJobWorker iniciado.");
        await foreach (var req in _queue.DequeueAllAsync(stoppingToken))
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var svc = scope.ServiceProvider.GetRequiredService<CloudiaKommoPatchService>();
                await svc.RunJobAsync(req.JobId, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                _logger.LogWarning("Worker encerrando. Job {JobId} interrompido.", req.JobId);
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Falha ao processar Kommo PATCH job {JobId}", req.JobId);
            }
        }
    }
}
