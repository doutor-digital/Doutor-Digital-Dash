using LeadAnalytics.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace LeadAnalytics.Api.Jobs;

/// <summary>
/// Roda a cada 1h. Encontra Treatments em status="aguardando_dados" há mais
/// de 24h sem preenchimento da SDR e dispara alerta para o gestor.
///
/// Notificação: por ora só loga em audit + InMemoryLogStore. Quando SignalR
/// estiver no ar, vira um push real-time pra dashboard do gestor.
/// </summary>
public class AlertaPreenchimentoPendenteJob : BackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromHours(1);
    private static readonly TimeSpan PendingThreshold = TimeSpan.FromHours(24);

    private readonly IServiceProvider _services;
    private readonly ILogger<AlertaPreenchimentoPendenteJob> _logger;

    public AlertaPreenchimentoPendenteJob(IServiceProvider services, ILogger<AlertaPreenchimentoPendenteJob> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try { await TickAsync(stoppingToken); }
            catch (Exception ex) { _logger.LogError(ex, "Falha em AlertaPreenchimentoPendenteJob"); }

            try { await Task.Delay(TickInterval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task TickAsync(CancellationToken ct)
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var cutoff = DateTime.UtcNow - PendingThreshold;

        var pending = await db.Treatments
            .AsNoTracking()
            .Where(t => t.Status == "aguardando_dados" && t.CreatedAt < cutoff)
            .Select(t => new { t.Id, t.LeadId, t.TenantId, t.UnitId, t.CreatedAt })
            .ToListAsync(ct);

        if (pending.Count == 0) return;

        foreach (var t in pending)
        {
            _logger.LogWarning(
                "🚨 Tratamento #{TreatmentId} sem preenchimento há {Hours:F1}h (lead={LeadId}, unit={UnitId})",
                t.Id, (DateTime.UtcNow - t.CreatedAt).TotalHours, t.LeadId, t.UnitId);
        }

        // TODO: quando SignalR estiver instalado, push pro hub do gestor.
        // TODO: opcional — email pro gestor (EmailService.SendAsync).
    }
}
