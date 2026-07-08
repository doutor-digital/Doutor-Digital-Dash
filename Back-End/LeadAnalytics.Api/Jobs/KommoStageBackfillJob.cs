using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.Service;
using Microsoft.EntityFrameworkCore;

namespace LeadAnalytics.Api.Jobs;

/// <summary>
/// Reconstrói o histórico de etapas com datas REAIS puxando os eventos
/// <c>lead_status_changed</c> da Kommo (<see cref="KommoStageHistoryBackfillService"/>).
///
/// Roda ~5min após o boot (depois do sync periódico estabilizar) e a cada 24h.
/// Idempotente (dedup por KommoEventId), então rodar repetido só preenche lacunas —
/// fecha buracos de transições que o webhook ao vivo eventualmente perdeu.
///
/// É o que repopula o KPI "agendados no dia" com datas corretas depois que as linhas
/// legadas (datadas por updated_at) deixaram de ser contadas.
/// </summary>
public class KommoStageBackfillJob : BackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromHours(24);
    private static readonly TimeSpan FirstTickDelay = TimeSpan.FromMinutes(5);
    private const int MaxPagesPerUnit = 300; // 300 × 100 = até 30k transições por unidade/execução
    private const int InterUnitDelayMs = 2000;

    private readonly IServiceProvider _services;
    private readonly ILogger<KommoStageBackfillJob> _logger;

    public KommoStageBackfillJob(IServiceProvider services, ILogger<KommoStageBackfillJob> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(FirstTickDelay, stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try { await TickAsync(stoppingToken); }
            catch (Exception ex) { _logger.LogError(ex, "Falha geral em KommoStageBackfillJob"); }

            try { await Task.Delay(TickInterval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task TickAsync(CancellationToken ct)
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var backfill = scope.ServiceProvider.GetRequiredService<KommoStageHistoryBackfillService>();
        var resgateBackfill = scope.ServiceProvider.GetRequiredService<ResgateAttemptBackfillService>();
        var qualifBackfill = scope.ServiceProvider.GetRequiredService<QualificationBackfillService>();

        var unitIds = await db.Units.AsNoTracking()
            .Where(u => u.IsActive
                        && u.KommoAccessToken != null && u.KommoAccessToken != ""
                        && u.KommoSubdomain != null && u.KommoSubdomain != "")
            .Select(u => u.Id)
            .ToListAsync(ct);

        if (unitIds.Count == 0)
        {
            _logger.LogInformation("[stage-backfill] nenhuma unidade ativa com token Kommo — skip");
            return;
        }

        _logger.LogInformation("[stage-backfill] iniciando: {Count} unidades", unitIds.Count);

        var totalInserted = 0;
        foreach (var id in unitIds)
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                var unit = await db.Units.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
                if (unit is null) continue;

                var r = await backfill.BackfillUnitAsync(unit, MaxPagesPerUnit, ct);
                totalInserted += r.Inserted;

                if (r.Error is not null)
                    _logger.LogWarning("[stage-backfill] unit={Unit} falhou: {Err}", id, r.Error);
                else
                    _logger.LogInformation(
                        "[stage-backfill] unit={Unit} ok: {Inserted} novas linhas de {Scanned} eventos (capTeto={Cap})",
                        id, r.Inserted, r.EventsScanned, r.HitCap);

                // Tentativas de resgate (mudança do campo "Tentativas de resgastes") — data real.
                var rr = await resgateBackfill.BackfillUnitAsync(unit, MaxPagesPerUnit, ct);
                totalInserted += rr.Inserted;
                if (rr.Error is not null)
                    _logger.LogWarning("[resgate-backfill] unit={Unit} falhou: {Err}", id, rr.Error);
                else
                    _logger.LogInformation(
                        "[resgate-backfill] unit={Unit} ok: {Inserted} tentativas de {Scanned} eventos (capTeto={Cap})",
                        id, rr.Inserted, rr.EventsScanned, rr.HitCap);

                // Qualificação (mudança do campo "Qualificação do lead") — data real de preenchimento.
                var rq = await qualifBackfill.BackfillUnitAsync(unit, MaxPagesPerUnit, ct);
                totalInserted += rq.Inserted;
                if (rq.Error is not null)
                    _logger.LogWarning("[qualif-backfill] unit={Unit} falhou: {Err}", id, rq.Error);
                else
                    _logger.LogInformation(
                        "[qualif-backfill] unit={Unit} ok: {Updated} leads de {Scanned} eventos (capTeto={Cap})",
                        id, rq.Inserted, rq.EventsScanned, rq.HitCap);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[stage-backfill] unit={Unit} exceção", id);
            }

            try { await Task.Delay(InterUnitDelayMs, ct); }
            catch (OperationCanceledException) { break; }
        }

        _logger.LogInformation("[stage-backfill] concluído: {Total} novas linhas inseridas", totalInserted);
    }
}
