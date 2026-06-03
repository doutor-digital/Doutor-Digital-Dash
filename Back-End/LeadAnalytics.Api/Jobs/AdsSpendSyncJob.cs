using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.Service.Ads;
using Microsoft.EntityFrameworkCore;

namespace LeadAnalytics.Api.Jobs;

/// <summary>
/// Job que, a cada 6h, re-sincroniza o gasto (últimos 30 dias) de todas as contas de Ads
/// conectadas. Mesmo padrão dos demais BackgroundService: delay inicial, loop com tick,
/// escopo de DI por iteração, erros logados sem derrubar o serviço.
/// </summary>
public class AdsSpendSyncJob(IServiceProvider services, ILogger<AdsSpendSyncJob> logger) : BackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromHours(6);
    private readonly IServiceProvider _services = services;
    private readonly ILogger<AdsSpendSyncJob> _logger = logger;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try { await TickAsync(stoppingToken); }
            catch (Exception ex) { _logger.LogError(ex, "Falha em AdsSpendSyncJob"); }

            try { await Task.Delay(TickInterval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task TickAsync(CancellationToken ct)
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var sync = scope.ServiceProvider.GetRequiredService<AdsSpendSyncService>();

        var accounts = await db.AdAccounts.Where(a => a.Status == "connected").ToListAsync(ct);
        if (accounts.Count == 0) return;

        var to = DateOnly.FromDateTime(DateTime.UtcNow);
        var from = to.AddDays(-30);
        foreach (var acct in accounts)
            await sync.SyncAsync(acct, from, to, ct);
    }
}
