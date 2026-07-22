using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.DTOs.Sync;
using LeadAnalytics.Api.Service.Ads;
using Microsoft.EntityFrameworkCore;

namespace LeadAnalytics.Api.Service;

/// <summary>
/// Orquestração dos syncs agendados, antes em BackgroundServices
/// (KommoSyncPeriodicJob, KommoNightlySyncJob, AdsSpendSyncJob).
///
/// A varredura e o ritmo (cron + espaçamento entre unidades p/ rate-limit)
/// agora vivem no n8n. Aqui só há "liste o que dá pra sincronizar" e
/// "sincronize ESTE item". A lógica de ingestão continua no KommoSyncService /
/// AdsSpendSyncService.
/// </summary>
public class ScheduledSyncService(
    AppDbContext db,
    KommoSyncService kommoSync,
    AdsSpendSyncService adsSync)
{
    private readonly AppDbContext _db = db;
    private readonly KommoSyncService _kommoSync = kommoSync;
    private readonly AdsSpendSyncService _adsSync = adsSync;

    // ---- Kommo ----

    /// <summary>Unidades ativas com token + subdomínio Kommo configurados.</summary>
    public async Task<List<KommoSyncUnitDto>> ListActiveKommoUnitsAsync(CancellationToken ct)
        => await _db.Units.AsNoTracking()
            .Where(u => u.IsActive
                        && u.KommoAccessToken != null && u.KommoAccessToken != ""
                        && u.KommoSubdomain != null && u.KommoSubdomain != "")
            .Select(u => new KommoSyncUnitDto { UnitId = u.Id, Name = u.Name })
            .ToListAsync(ct);

    /// <summary>
    /// Sincroniza uma única unidade. <paramref name="maxLeads"/> controla a
    /// profundidade: ~500 no sync incremental (30min), ~5000 no profundo (noturno).
    /// </summary>
    public async Task<KommoUnitSyncResultDto> SyncKommoUnitAsync(int unitId, int maxLeads, CancellationToken ct)
    {
        var unit = await _db.Units.AsNoTracking().FirstOrDefaultAsync(u => u.Id == unitId, ct);
        if (unit is null)
            return new KommoUnitSyncResultDto { UnitId = unitId, Error = "unidade não encontrada" };

        if (string.IsNullOrWhiteSpace(unit.KommoAccessToken) || string.IsNullOrWhiteSpace(unit.KommoSubdomain))
            return new KommoUnitSyncResultDto { UnitId = unitId, Name = unit.Name, Error = "unidade sem token/subdomínio Kommo" };

        var r = await _kommoSync.SyncAsync(unit, unit.KommoAccessToken, maxLeads, ct);
        return new KommoUnitSyncResultDto
        {
            UnitId = unit.Id,
            Name = unit.Name,
            LeadsFetched = r.LeadsFetched,
            LeadsPersisted = r.LeadsPersisted,
            DurationMs = r.DurationMs,
            Error = r.Error
        };
    }

    // ---- Ads ----

    /// <summary>Contas de Ads conectadas.</summary>
    public async Task<List<AdAccountRefDto>> ListConnectedAdAccountsAsync(CancellationToken ct)
        => await _db.AdAccounts.AsNoTracking()
            .Where(a => a.Status == "connected")
            .Select(a => new AdAccountRefDto { AccountId = a.Id, Name = a.Name, Provider = a.Provider })
            .ToListAsync(ct);

    /// <summary>Re-sincroniza o gasto dos últimos 30 dias de uma conta de Ads.</summary>
    public async Task<AdAccountSyncResultDto> SyncAdAccountAsync(int accountId, CancellationToken ct)
    {
        // Tracked de propósito: AdsSpendSyncService carimba LastSyncAt na conta.
        var acct = await _db.AdAccounts.FirstOrDefaultAsync(a => a.Id == accountId, ct);
        if (acct is null)
            return new AdAccountSyncResultDto { AccountId = accountId, Error = "conta não encontrada" };

        var to = DateOnly.FromDateTime(DateTime.UtcNow);
        var from = to.AddDays(-30);
        var rows = await _adsSync.SyncAsync(acct, from, to, ct);

        return new AdAccountSyncResultDto
        {
            AccountId = acct.Id,
            Name = acct.Name,
            Provider = acct.Provider,
            RowsUpserted = rows
        };
    }
}
