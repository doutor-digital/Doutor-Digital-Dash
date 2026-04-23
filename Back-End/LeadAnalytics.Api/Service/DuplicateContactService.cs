using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.DTOs.Admin;
using Microsoft.EntityFrameworkCore;

namespace LeadAnalytics.Api.Service;

public class DuplicateContactService(AppDbContext db, ILogger<DuplicateContactService> logger)
{
    private readonly AppDbContext _db = db;
    private readonly ILogger<DuplicateContactService> _logger = logger;

    private sealed record ContactRow(int Id, string Name, string PhoneNormalized, DateTime CreatedAt, int TenantId);

    public async Task<DuplicatesReportDto> FindDuplicatesAsync(
        int? tenantId,
        bool ignoreTenant,
        CancellationToken ct = default)
    {
        var query = _db.Contacts.AsNoTracking().AsQueryable();
        if (!ignoreTenant && tenantId.HasValue)
            query = query.Where(c => c.TenantId == tenantId.Value);

        List<DuplicateGroupDto> groups;

        if (ignoreTenant)
        {
            // 1 query: telefones com duplicata.
            var duplicatePhones = await query
                .GroupBy(c => c.PhoneNormalized)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToListAsync(ct);

            if (duplicatePhones.Count == 0)
                return Empty();

            // 1 query: TODAS as linhas dos telefones duplicados.
            var rows = await query
                .Where(c => duplicatePhones.Contains(c.PhoneNormalized))
                .OrderBy(c => c.PhoneNormalized)
                .ThenBy(c => c.CreatedAt)
                .ThenBy(c => c.Id)
                .Select(c => new ContactRow(c.Id, c.Name, c.PhoneNormalized, c.CreatedAt, c.TenantId))
                .ToListAsync(ct);

            // Agrupa em memória.
            groups = rows
                .GroupBy(r => r.PhoneNormalized)
                .Where(g => g.Count() > 1)
                .Select(g =>
                {
                    var ordered = g.ToList();
                    var keep = ordered[0];
                    return new DuplicateGroupDto
                    {
                        TenantId = keep.TenantId,
                        PhoneNormalized = g.Key,
                        Count = ordered.Count,
                        KeepContactId = keep.Id,
                        KeepName = keep.Name,
                        KeepCreatedAt = keep.CreatedAt,
                        DeleteContactIds = ordered.Skip(1).Select(r => r.Id).ToList(),
                    };
                })
                .ToList();
        }
        else
        {
            // 1 query: chaves (tenant, phone) com duplicata.
            var duplicatePhones = await query
                .GroupBy(c => new { c.TenantId, c.PhoneNormalized })
                .Where(g => g.Count() > 1)
                .Select(g => g.Key.PhoneNormalized)
                .Distinct()
                .ToListAsync(ct);

            if (duplicatePhones.Count == 0)
                return Empty();

            // 1 query: todas as linhas desses telefones (pode pegar mais do que precisa
            // quando o tenant é outro; filtramos em memória logo a seguir).
            var rows = await query
                .Where(c => duplicatePhones.Contains(c.PhoneNormalized))
                .OrderBy(c => c.TenantId)
                .ThenBy(c => c.PhoneNormalized)
                .ThenBy(c => c.CreatedAt)
                .ThenBy(c => c.Id)
                .Select(c => new ContactRow(c.Id, c.Name, c.PhoneNormalized, c.CreatedAt, c.TenantId))
                .ToListAsync(ct);

            groups = rows
                .GroupBy(r => new { r.TenantId, r.PhoneNormalized })
                .Where(g => g.Count() > 1)
                .Select(g =>
                {
                    var ordered = g.ToList();
                    var keep = ordered[0];
                    return new DuplicateGroupDto
                    {
                        TenantId = g.Key.TenantId,
                        PhoneNormalized = g.Key.PhoneNormalized,
                        Count = ordered.Count,
                        KeepContactId = keep.Id,
                        KeepName = keep.Name,
                        KeepCreatedAt = keep.CreatedAt,
                        DeleteContactIds = ordered.Skip(1).Select(r => r.Id).ToList(),
                    };
                })
                .ToList();
        }

        _logger.LogInformation(
            "Duplicados achados: {Groups} grupo(s), {Delete} a apagar (tenantId={TenantId}, ignoreTenant={Ignore})",
            groups.Count, groups.Sum(g => g.DeleteContactIds.Count), tenantId, ignoreTenant);

        return new DuplicatesReportDto
        {
            DryRun = true,
            GroupsFound = groups.Count,
            ContactsToDelete = groups.Sum(g => g.DeleteContactIds.Count),
            Groups = groups,
        };
    }

    public async Task<DuplicatesReportDto> DeleteDuplicatesAsync(
        int? tenantId,
        bool ignoreTenant,
        CancellationToken ct = default)
    {
        var report = await FindDuplicatesAsync(tenantId, ignoreTenant, ct);
        report.DryRun = false;

        var idsToDelete = report.Groups.SelectMany(g => g.DeleteContactIds).ToList();
        if (idsToDelete.Count == 0) return report;

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            // Apaga em batches para não estourar o limite de parâmetros do Postgres (~32k).
            const int batchSize = 2_000;
            int totalDeleted = 0;
            for (int i = 0; i < idsToDelete.Count; i += batchSize)
            {
                var batch = idsToDelete.Skip(i).Take(batchSize).ToList();
                totalDeleted += await _db.Contacts
                    .Where(c => batch.Contains(c.Id))
                    .ExecuteDeleteAsync(ct);
            }

            await tx.CommitAsync(ct);

            _logger.LogWarning(
                "🗑 Contacts duplicados apagados: {Deleted} linhas em {Groups} grupo(s) (tenantId={TenantId}, ignoreTenant={IgnoreTenant})",
                totalDeleted, report.GroupsFound, tenantId, ignoreTenant);

            report.ContactsToDelete = totalDeleted;
            return report;
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    private static DuplicatesReportDto Empty() => new()
    {
        DryRun = true,
        GroupsFound = 0,
        ContactsToDelete = 0,
        Groups = [],
    };
}
