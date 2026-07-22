using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.Models;

namespace LeadAnalytics.Api.Service;

public class AuditLogService
{
    private readonly AppDbContext _db;

    public AuditLogService(AppDbContext db)
    {
        _db = db;
    }

    public async Task WriteAsync(AuditLog log, CancellationToken ct = default)
    {
        _db.AuditLogs.Add(log);
        await _db.SaveChangesAsync(ct);
    }
}
