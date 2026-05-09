using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.Models;
using LeadAnalytics.Api.Service.Stages;
using Microsoft.EntityFrameworkCore;

namespace LeadAnalytics.Api.Jobs;

/// <summary>
/// Roda a cada 30s. Pega envelopes pending/falhados (cujo NextAttemptAt já passou)
/// e despacha pelo <see cref="StageWebhookDispatcher"/>.
///
/// Retry: backoff exponencial (5s, 25s, 2min, 10min, 1h). MaxAttempts=5.
/// </summary>
public class ProcessarWebhooksJob : BackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(30);
    private const int BatchSize = 25;
    private const int MaxAttempts = 5;

    private readonly IServiceProvider _services;
    private readonly ILogger<ProcessarWebhooksJob> _logger;

    public ProcessarWebhooksJob(IServiceProvider services, ILogger<ProcessarWebhooksJob> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Aguarda o app subir antes de começar a martelar o banco.
        try { await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await TickAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Falha geral no ProcessarWebhooksJob");
            }

            try { await Task.Delay(TickInterval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task TickAsync(CancellationToken ct)
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var dispatcher = scope.ServiceProvider.GetRequiredService<StageWebhookDispatcher>();

        var now = DateTime.UtcNow;

        // Pega pendentes cujo NextAttemptAt já passou. ORDER BY OccurredAt pra processar em ordem.
        var batch = await db.WebhookEnvelopes
            .Where(e => e.Status == "pending"
                     && (e.NextAttemptAt == null || e.NextAttemptAt <= now))
            .OrderBy(e => e.OccurredAt)
            .Take(BatchSize)
            .ToListAsync(ct);

        if (batch.Count == 0) return;

        _logger.LogDebug("ProcessarWebhooksJob: pegando {Count} envelopes", batch.Count);

        foreach (var env in batch)
        {
            // Marca como processing (otimista — sem row lock; em escala usar SELECT FOR UPDATE SKIP LOCKED).
            env.Status = "processing";
            env.Attempts++;
            await db.SaveChangesAsync(ct);

            try
            {
                await dispatcher.DispatchAsync(env, ct);
                env.Status = "done";
                env.ProcessedAt = DateTime.UtcNow;
                env.LastError = null;
                _logger.LogInformation("✅ Envelope #{Id} processado", env.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Falha ao processar envelope #{Id}", env.Id);
                env.LastError = Truncate(ex.ToString(), 2000);

                if (env.Attempts >= MaxAttempts)
                {
                    env.Status = "failed";
                    _logger.LogError("Envelope #{Id} desistido após {Attempts} tentativas", env.Id, env.Attempts);
                }
                else
                {
                    env.Status = "pending";
                    env.NextAttemptAt = now + BackoffFor(env.Attempts);
                }
            }
            finally
            {
                await db.SaveChangesAsync(ct);
            }
        }
    }

    /// <summary>5s, 25s, 2min, 10min, 1h.</summary>
    private static TimeSpan BackoffFor(int attempt) => attempt switch
    {
        1 => TimeSpan.FromSeconds(5),
        2 => TimeSpan.FromSeconds(25),
        3 => TimeSpan.FromMinutes(2),
        4 => TimeSpan.FromMinutes(10),
        _ => TimeSpan.FromHours(1),
    };

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max];
}
