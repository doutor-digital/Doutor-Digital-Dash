using LeadAnalytics.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace LeadAnalytics.Api.Jobs;

/// <summary>
/// Roda diário. Marca TreatmentInstallment com DueDate &lt; hoje e Status="pendente"
/// como Status="atrasado", e loga alerta.
/// </summary>
public class AlertaPagamentoAtrasadoJob : BackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromHours(24);
    private readonly IServiceProvider _services;
    private readonly ILogger<AlertaPagamentoAtrasadoJob> _logger;

    public AlertaPagamentoAtrasadoJob(IServiceProvider services, ILogger<AlertaPagamentoAtrasadoJob> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try { await TickAsync(stoppingToken); }
            catch (Exception ex) { _logger.LogError(ex, "Falha em AlertaPagamentoAtrasadoJob"); }

            try { await Task.Delay(TickInterval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task TickAsync(CancellationToken ct)
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var overdue = await db.TreatmentInstallments
            .Where(i => i.Status == "pendente"
                     && i.DueDate != null
                     && i.DueDate < today)
            .ToListAsync(ct);

        if (overdue.Count == 0) return;

        foreach (var i in overdue)
        {
            i.Status = "atrasado";
            _logger.LogWarning(
                "💰 Parcela #{Id} (treatment={TreatmentId}) atrasada desde {Due:yyyy-MM-dd}",
                i.Id, i.TreatmentId, i.DueDate);
        }

        await db.SaveChangesAsync(ct);

        // TODO: SignalR / email — disparar quando estiver no ar.
    }
}
