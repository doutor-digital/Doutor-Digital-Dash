using LeadAnalytics.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;

namespace LeadAnalytics.Api.Jobs;

/// <summary>
/// Roda a cada 1h. Recalcula KPIs agregados do dashboard e grava no cache
/// distribuído (Redis em prod, MemoryCache local em dev).
///
/// KPIs publicados (chave = "kpis:tenant:{tenantId}"):
///   - leadsToday, leadsLast7d, leadsLast30d
///   - consultationsToday, consultationsLast30d
///   - treatmentsAwaitingData, treatmentsAwaitingApproval, treatmentsApproved
///   - revenueLast30d (soma de TotalValue de treatments aprovados)
///
/// Nota: o front pode bater num endpoint que devolve esse cache,
/// evitando refazer COUNT(*) a cada page load.
/// </summary>
public class RecalculoKpisJob : BackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromHours(1);

    private readonly IServiceProvider _services;
    private readonly ILogger<RecalculoKpisJob> _logger;

    public RecalculoKpisJob(IServiceProvider services, ILogger<RecalculoKpisJob> logger)
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
            catch (Exception ex) { _logger.LogError(ex, "Falha em RecalculoKpisJob"); }

            try { await Task.Delay(TickInterval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task TickAsync(CancellationToken ct)
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var cache = scope.ServiceProvider.GetRequiredService<IDistributedCache>();

        var tenants = await db.Units
            .AsNoTracking()
            .Select(u => u.ClinicId)
            .Distinct()
            .ToListAsync(ct);

        var now = DateTime.UtcNow;
        var today = now.Date;
        var last7d = today.AddDays(-7);
        var last30d = today.AddDays(-30);

        foreach (var tenantId in tenants)
        {
            var leadsToday = await db.Leads.CountAsync(l => l.TenantId == tenantId && l.CreatedAt >= today, ct);
            var leadsL7 = await db.Leads.CountAsync(l => l.TenantId == tenantId && l.CreatedAt >= last7d, ct);
            var leadsL30 = await db.Leads.CountAsync(l => l.TenantId == tenantId && l.CreatedAt >= last30d, ct);

            var consultsToday = await db.Consultations.CountAsync(c => c.TenantId == tenantId && c.CreatedAt >= today, ct);
            var consultsL30 = await db.Consultations.CountAsync(c => c.TenantId == tenantId && c.CreatedAt >= last30d, ct);

            var awaitingData = await db.Treatments.CountAsync(t => t.TenantId == tenantId && t.Status == "aguardando_dados", ct);
            var awaitingApproval = await db.Treatments.CountAsync(t => t.TenantId == tenantId && t.Status == "aguardando_aprovacao", ct);
            var approved = await db.Treatments.CountAsync(t => t.TenantId == tenantId && t.Status == "aprovado", ct);

            var revenueL30 = await db.Treatments
                .Where(t => t.TenantId == tenantId
                         && t.Status == "aprovado"
                         && t.DecidedAt >= last30d
                         && t.TotalValue != null)
                .SumAsync(t => t.TotalValue ?? 0m, ct);

            var json = System.Text.Json.JsonSerializer.Serialize(new
            {
                tenantId,
                computedAt = now,
                leadsToday, leadsL7, leadsL30,
                consultsToday, consultsL30,
                awaitingData, awaitingApproval, approved,
                revenueL30,
            });

            await cache.SetStringAsync(
                $"kpis:tenant:{tenantId}",
                json,
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(2) },
                ct);
        }

        _logger.LogInformation("📊 KPIs recalculados para {Count} tenants", tenants.Count);
    }
}
