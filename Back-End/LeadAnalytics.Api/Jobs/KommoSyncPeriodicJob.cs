using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.Service;
using Microsoft.EntityFrameworkCore;

namespace LeadAnalytics.Api.Jobs;

/// <summary>
/// Roda a cada 30min varrendo todas as unidades ativas que têm token Kommo
/// configurado e dispara <see cref="KommoSyncService.SyncAsync"/> em cada uma.
/// É o sync periódico de leads/contatos/custom_fields que o webhook não cobre.
///
/// <para><b>Por que existe</b></para>
/// O webhook do Kommo manda payload <b>parcial</b> — não traz a lista de
/// custom_fields_values do lead atualizado. Resultado prático: o
/// <c>Lead.CustomFieldsJson</c> só era preenchido se alguém clicasse no
/// "Sincronizar" lá em /units. Sintoma: cards de "Campos da Kommo" vazios
/// no dashboard mesmo com atendentes preenchendo na Kommo o dia inteiro.
/// Este job resolve isso varrendo as unidades de tempos em tempos.
///
/// <para><b>Cuidados</b></para>
/// <list type="bullet">
/// <item><c>MaxLeadsPerTick = 500</c> — pega só os 500 leads mais recentes
/// por unidade. Reduzido a propósito: o sync é incremental e a janela de
/// 30min raramente mexe em mais que isso. Sync grande (5k) continua manual.</item>
/// <item><c>InterUnitDelayMs = 2000</c> — 2s entre unidades pra não estourar
/// o rate-limit de 7 RPS da Kommo (cada unidade é uma conta diferente, mas
/// vale a paciência mesmo assim).</item>
/// <item>Erro em uma unidade não para o job — só loga e segue pra próxima.</item>
/// </list>
/// </summary>
public class KommoSyncPeriodicJob : BackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan FirstTickDelay = TimeSpan.FromMinutes(2);
    private const int MaxLeadsPerTick = 500;
    private const int InterUnitDelayMs = 2000;

    private readonly IServiceProvider _services;
    private readonly ILogger<KommoSyncPeriodicJob> _logger;

    public KommoSyncPeriodicJob(IServiceProvider services, ILogger<KommoSyncPeriodicJob> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Espera o app estabilizar antes do 1º tick — evita atropelar startup.
        try { await Task.Delay(FirstTickDelay, stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try { await TickAsync(stoppingToken); }
            catch (Exception ex) { _logger.LogError(ex, "Falha geral em KommoSyncPeriodicJob"); }

            try { await Task.Delay(TickInterval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
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
            .Select(u => new { u.Id, u.Name, u.KommoSubdomain, u.KommoAccessToken, u.ClinicId })
            .ToListAsync(ct);

        if (units.Count == 0)
        {
            _logger.LogInformation("[kommo-sync-job] nenhuma unidade ativa com token Kommo — skip");
            return;
        }

        _logger.LogInformation(
            "[kommo-sync-job] iniciando tick: {Count} unidades, maxLeads={Max}",
            units.Count, MaxLeadsPerTick);

        var totalLeads = 0;
        var ok = 0;
        var fail = 0;

        foreach (var u in units)
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                // Reanexa a Unit num scope/tracker novo pra não compartilhar
                // entidades entre iterações (cada SyncAsync internamente abre
                // suas próprias mudanças no DbContext).
                var unit = await db.Units.AsNoTracking().FirstOrDefaultAsync(x => x.Id == u.Id, ct);
                if (unit is null) continue;

                var result = await sync.SyncAsync(unit, u.KommoAccessToken!, MaxLeadsPerTick, ct);

                if (!string.IsNullOrEmpty(result.Error))
                {
                    fail++;
                    _logger.LogWarning(
                        "[kommo-sync-job] unit={Unit} ({Name}) falhou: {Err}",
                        u.Id, u.Name, result.Error);
                }
                else
                {
                    ok++;
                    totalLeads += result.LeadsPersisted;
                    _logger.LogInformation(
                        "[kommo-sync-job] unit={Unit} ({Name}) ok: {Persisted}/{Fetched} leads em {Ms}ms",
                        u.Id, u.Name, result.LeadsPersisted, result.LeadsFetched, result.DurationMs);
                }
            }
            catch (Exception ex)
            {
                fail++;
                _logger.LogWarning(ex, "[kommo-sync-job] unit={Unit} ({Name}) exceção", u.Id, u.Name);
            }

            // Respeita o rate-limit da Kommo (7 RPS por conta) — paciência entre unidades.
            try { await Task.Delay(InterUnitDelayMs, ct); }
            catch (OperationCanceledException) { break; }
        }

        _logger.LogInformation(
            "[kommo-sync-job] tick concluído: ok={Ok} fail={Fail} leads_persistidos={Total}",
            ok, fail, totalLeads);
    }
}
