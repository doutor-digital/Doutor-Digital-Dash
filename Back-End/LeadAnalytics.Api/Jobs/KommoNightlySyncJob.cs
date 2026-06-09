using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.Service;
using Microsoft.EntityFrameworkCore;

namespace LeadAnalytics.Api.Jobs;

/// <summary>
/// Sync PROFUNDO diário — roda toda madrugada (~03h BRT) varrendo as unidades
/// ativas com token Kommo e puxando uma janela grande de leads (5000) por unidade.
///
/// <para><b>Por que existe</b></para>
/// O <see cref="KommoSyncPeriodicJob"/> roda a cada 30min mas só pega os 500 leads
/// mais recentes (incremental, leve). Leads antigos editados na Kommo fora dessa
/// janela podiam ficar desatualizados no nosso banco. Este job, na madrugada
/// (movimento baixo, sem competir com o uso do dia), refaz um backfill amplo pra
/// reconciliar tudo. Complementa — não substitui — o periódico.
///
/// <para><b>Agenda</b></para>
/// BRT é UTC-3 fixo (Brasil aboliu horário de verão em 2019). 03h BRT = 06h UTC.
/// O job dorme até a próxima ocorrência de 06:00 UTC e roda 1×/dia.
/// </summary>
public class KommoNightlySyncJob : BackgroundService
{
    // 03h BRT = 06h UTC. Hora do dia (UTC) em que o sync profundo dispara.
    private const int RunHourUtc = 6;
    private const int MaxLeadsPerUnit = 5000;
    private const int InterUnitDelayMs = 2000;

    private readonly IServiceProvider _services;
    private readonly ILogger<KommoNightlySyncJob> _logger;

    public KommoNightlySyncJob(IServiceProvider services, ILogger<KommoNightlySyncJob> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = TimeUntilNextRun(DateTime.UtcNow);
            _logger.LogInformation(
                "[kommo-nightly] próximo sync profundo em {Hours:F1}h (alvo 03h BRT)",
                delay.TotalHours);

            try { await Task.Delay(delay, stoppingToken); }
            catch (OperationCanceledException) { break; }

            try { await TickAsync(stoppingToken); }
            catch (Exception ex) { _logger.LogError(ex, "Falha geral em KommoNightlySyncJob"); }
        }
    }

    /// <summary>Tempo até a próxima 06:00 UTC (= 03h BRT).</summary>
    private static TimeSpan TimeUntilNextRun(DateTime nowUtc)
    {
        var todayRun = new DateTime(nowUtc.Year, nowUtc.Month, nowUtc.Day, RunHourUtc, 0, 0, DateTimeKind.Utc);
        var nextRun = nowUtc < todayRun ? todayRun : todayRun.AddDays(1);
        return nextRun - nowUtc;
    }

    private async Task TickAsync(CancellationToken ct)
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var sync = scope.ServiceProvider.GetRequiredService<KommoSyncService>();

        var units = await db.Units
            .AsNoTracking()
            .Where(u => u.IsActive
                        && u.KommoAccessToken != null && u.KommoAccessToken != ""
                        && u.KommoSubdomain != null && u.KommoSubdomain != "")
            .Select(u => new { u.Id, u.Name, u.KommoAccessToken })
            .ToListAsync(ct);

        if (units.Count == 0)
        {
            _logger.LogInformation("[kommo-nightly] nenhuma unidade ativa com token Kommo — skip");
            return;
        }

        _logger.LogInformation(
            "[kommo-nightly] iniciando sync profundo: {Count} unidades, maxLeads={Max}",
            units.Count, MaxLeadsPerUnit);

        var totalLeads = 0;
        var ok = 0;
        var fail = 0;

        foreach (var u in units)
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                var unit = await db.Units.AsNoTracking().FirstOrDefaultAsync(x => x.Id == u.Id, ct);
                if (unit is null) continue;

                var result = await sync.SyncAsync(unit, u.KommoAccessToken!, MaxLeadsPerUnit, ct);

                if (!string.IsNullOrEmpty(result.Error))
                {
                    fail++;
                    _logger.LogWarning(
                        "[kommo-nightly] unit={Unit} ({Name}) falhou: {Err}",
                        u.Id, u.Name, result.Error);
                }
                else
                {
                    ok++;
                    totalLeads += result.LeadsPersisted;
                    _logger.LogInformation(
                        "[kommo-nightly] unit={Unit} ({Name}) ok: {Persisted}/{Fetched} leads em {Ms}ms",
                        u.Id, u.Name, result.LeadsPersisted, result.LeadsFetched, result.DurationMs);
                }
            }
            catch (Exception ex)
            {
                fail++;
                _logger.LogWarning(ex, "[kommo-nightly] unit={Unit} ({Name}) exceção", u.Id, u.Name);
            }

            try { await Task.Delay(InterUnitDelayMs, ct); }
            catch (OperationCanceledException) { break; }
        }

        _logger.LogInformation(
            "[kommo-nightly] sync profundo concluído: ok={Ok} fail={Fail} leads_persistidos={Total}",
            ok, fail, totalLeads);
    }
}
