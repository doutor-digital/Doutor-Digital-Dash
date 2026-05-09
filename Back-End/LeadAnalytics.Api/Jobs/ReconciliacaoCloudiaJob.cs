namespace LeadAnalytics.Api.Jobs;

/// <summary>
/// Roda diário (madrugada). Busca leads/etapas direto da API de saída da Cloudia
/// e reconcilia com o banco — captura webhooks que se perderam.
///
/// STATUS: stub. A Cloudia hoje só expõe webhook de entrada. Quando a API
/// outbound for confirmada, implementar:
///   1. Pra cada Tenant ativo: GET /customers?updated_after={lastSync}
///   2. Comparar etapa atual com Lead.CurrentStage
///   3. Se diferente, gerar um WebhookEnvelope sintético com o estado correto
///   4. Atualizar lastSync por tenant
/// </summary>
public class ReconciliacaoCloudiaJob : BackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromHours(24);
    private readonly ILogger<ReconciliacaoCloudiaJob> _logger;

    public ReconciliacaoCloudiaJob(ILogger<ReconciliacaoCloudiaJob> logger)
    {
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("🔄 ReconciliacaoCloudiaJob — stub (API outbound não confirmada)");
            // TODO: implementar quando a Cloudia expuser endpoint GET /customers.

            try { await Task.Delay(TickInterval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}
