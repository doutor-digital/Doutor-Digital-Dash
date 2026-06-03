using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace LeadAnalytics.Api.Service.Ads;

/// <summary>
/// Sincroniza o gasto de uma conta de anúncios: chama o provedor, faz UPSERT em
/// <see cref="CampaignDailySpend"/> por (conta, campanha, dia) e atualiza o último sync.
/// </summary>
public class AdsSpendSyncService(
    AppDbContext db,
    IEnumerable<IAdsProvider> providers,
    AdsCredentialsService credentials,
    ILogger<AdsSpendSyncService> logger)
{
    private readonly AppDbContext _db = db;
    private readonly IEnumerable<IAdsProvider> _providers = providers;
    private readonly AdsCredentialsService _credentials = credentials;
    private readonly ILogger<AdsSpendSyncService> _logger = logger;

    public async Task<int> SyncAccountAsync(int accountId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var acct = await _db.AdAccounts.FirstOrDefaultAsync(a => a.Id == accountId, ct);
        if (acct is null || acct.Status != "connected") return 0;
        return await SyncAsync(acct, from, to, ct);
    }

    public async Task<int> SyncAsync(AdAccount acct, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var provider = _providers.FirstOrDefault(p => p.Provider == acct.Provider);
        if (provider is null) return 0;
        var creds = await _credentials.GetAsync(acct.Provider, ct);

        IReadOnlyList<CampaignSpendRow> rows;
        try
        {
            rows = await provider.FetchDailySpendAsync(creds, acct, from, to, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Falha ao sincronizar gasto da conta {Id} ({Provider})", acct.Id, acct.Provider);
            acct.LastSyncNote = "erro: " + ex.Message;
            acct.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            return 0;
        }

        var existing = await _db.CampaignDailySpends
            .Where(s => s.AdAccountId == acct.Id && s.Date >= from && s.Date <= to)
            .ToListAsync(ct);
        var map = existing.ToDictionary(s => (s.CampaignId, s.Date));

        var now = DateTime.UtcNow;
        foreach (var r in rows)
        {
            if (map.TryGetValue((r.CampaignId, r.Date), out var row))
            {
                row.Spend = r.Spend;
                row.CampaignName = r.CampaignName;
                row.Currency = r.Currency;
                row.SyncedAt = now;
            }
            else
            {
                _db.CampaignDailySpends.Add(new CampaignDailySpend
                {
                    ClinicId = acct.ClinicId,
                    AdAccountId = acct.Id,
                    Provider = acct.Provider,
                    CampaignId = r.CampaignId,
                    CampaignName = r.CampaignName,
                    Date = r.Date,
                    Spend = r.Spend,
                    Currency = r.Currency,
                    SyncedAt = now,
                });
            }
        }

        acct.LastSyncAt = now;
        acct.LastSyncNote = $"{rows.Count} linhas";
        acct.UpdatedAt = now;
        await _db.SaveChangesAsync(ct);
        return rows.Count;
    }
}
