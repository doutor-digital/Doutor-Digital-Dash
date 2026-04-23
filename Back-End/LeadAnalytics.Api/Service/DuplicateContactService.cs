using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.DTOs.Admin;
using Microsoft.EntityFrameworkCore;

namespace LeadAnalytics.Api.Service;

public class DuplicateContactService(AppDbContext db, ILogger<DuplicateContactService> logger)
{
    private readonly AppDbContext _db = db;
    private readonly ILogger<DuplicateContactService> _logger = logger;

    public async Task<DuplicatesReportDto> FindDuplicatesAsync(int? tenantId, CancellationToken ct = default)
    {
        var query = _db.Contacts.AsNoTracking().AsQueryable();
        if (tenantId.HasValue)
            query = query.Where(c => c.TenantId == tenantId.Value);

        var duplicateKeys = await query
            .GroupBy(c => new { c.TenantId, c.PhoneNormalized })
            .Where(g => g.Count() > 1)
            .Select(g => new { g.Key.TenantId, g.Key.PhoneNormalized })
            .ToListAsync(ct);

        var groups = new List<DuplicateGroupDto>(duplicateKeys.Count);
        foreach (var key in duplicateKeys)
        {
            var rows = await query
                .Where(c => c.TenantId == key.TenantId && c.PhoneNormalized == key.PhoneNormalized)
                .OrderBy(c => c.CreatedAt)
                .ThenBy(c => c.Id)
                .Select(c => new { c.Id, c.Name, c.CreatedAt })
                .ToListAsync(ct);

            if (rows.Count <= 1) continue;

            var keep = rows[0];
            groups.Add(new DuplicateGroupDto
            {
                TenantId = key.TenantId,
                PhoneNormalized = key.PhoneNormalized,
                Count = rows.Count,
                KeepContactId = keep.Id,
                KeepName = keep.Name,
                KeepCreatedAt = keep.CreatedAt,
                DeleteContactIds = rows.Skip(1).Select(r => r.Id).ToList()
            });
        }

        return new DuplicatesReportDto
        {
            DryRun = true,
            GroupsFound = groups.Count,
            ContactsToDelete = groups.Sum(g => g.DeleteContactIds.Count),
            Groups = groups
        };
    }

    public async Task<DuplicatesReportDto> DeleteDuplicatesAsync(int? tenantId, CancellationToken ct = default)
    {
        var report = await FindDuplicatesAsync(tenantId, ct);
        report.DryRun = false;

        var idsToDelete = report.Groups.SelectMany(g => g.DeleteContactIds).ToList();
        if (idsToDelete.Count == 0) return report;

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            var deleted = await _db.Contacts
                .Where(c => idsToDelete.Contains(c.Id))
                .ExecuteDeleteAsync(ct);

            await tx.CommitAsync(ct);

            _logger.LogWarning(
                "🗑 Contacts duplicados apagados: {Deleted} linhas em {Groups} grupo(s) (tenantId={TenantId})",
                deleted, report.GroupsFound, tenantId);

            report.ContactsToDelete = deleted;
            return report;
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }
}
