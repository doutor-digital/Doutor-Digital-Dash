using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.DTOs.Audit;
using LeadAnalytics.Api.Models;
using Microsoft.EntityFrameworkCore;

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

    public async Task<AuditLogPageDto> QueryAsync(
        DateTime? from,
        DateTime? to,
        int? userId,
        string? email,
        string? path,
        string? ip,
        int? statusCode,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 50;
        if (pageSize > 500) pageSize = 500;

        var q = _db.AuditLogs.AsNoTracking().AsQueryable();

        if (from.HasValue) q = q.Where(a => a.CreatedAt >= from.Value);
        if (to.HasValue) q = q.Where(a => a.CreatedAt <= to.Value);
        if (userId.HasValue) q = q.Where(a => a.UserId == userId.Value);
        if (!string.IsNullOrWhiteSpace(email))
        {
            var em = email.Trim().ToLower();
            q = q.Where(a => a.Email != null && a.Email.ToLower().Contains(em));
        }
        if (!string.IsNullOrWhiteSpace(path))
        {
            var p = path.Trim();
            q = q.Where(a => a.Path.Contains(p));
        }
        if (!string.IsNullOrWhiteSpace(ip))
        {
            var ipFilter = ip.Trim();
            q = q.Where(a => a.Ip != null && a.Ip.Contains(ipFilter));
        }
        if (statusCode.HasValue) q = q.Where(a => a.StatusCode == statusCode.Value);

        var total = await q.CountAsync(ct);

        var items = await q
            .OrderByDescending(a => a.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(a => new AuditLogItemDto
            {
                Id = a.Id,
                UserId = a.UserId,
                Email = a.Email,
                UserName = a.UserName,
                Role = a.Role,
                TenantId = a.TenantId,
                AuthMethod = a.AuthMethod,
                Ip = a.Ip,
                UserAgent = a.UserAgent,
                Method = a.Method,
                Path = a.Path,
                QueryString = a.QueryString,
                StatusCode = a.StatusCode,
                DurationMs = a.DurationMs,
                CreatedAt = a.CreatedAt
            })
            .ToListAsync(ct);

        return new AuditLogPageDto
        {
            Items = items,
            Total = total,
            Page = page,
            PageSize = pageSize
        };
    }
}
