using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.DTOs.Admin;
using Microsoft.EntityFrameworkCore;

namespace LeadAnalytics.Api.Service;

/// <summary>
/// Consultas do controle de log avançado (visível a super_admin / analista_ti):
/// sessões de login (IP, geo, minutos), trilha de alteração e consentimentos de
/// localização. Espelha o padrão paginado de <see cref="AuditLogService"/>.
/// </summary>
public class AdminLogService
{
    private readonly AppDbContext _db;

    public AdminLogService(AppDbContext db) => _db = db;

    public async Task<LoginSessionPageDto> QueryLoginSessionsAsync(
        DateTime? from, DateTime? to, int? userId, string? email, bool? active,
        int page, int pageSize, CancellationToken ct = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 50;
        if (pageSize > 500) pageSize = 500;

        var q = _db.LoginSessions.AsNoTracking().AsQueryable();

        if (from.HasValue) q = q.Where(s => s.LoginAt >= from.Value);
        if (to.HasValue) q = q.Where(s => s.LoginAt <= to.Value);
        if (userId.HasValue) q = q.Where(s => s.UserId == userId.Value);
        if (!string.IsNullOrWhiteSpace(email))
        {
            var em = email.Trim().ToLower();
            q = q.Where(s => s.Email != null && s.Email.ToLower().Contains(em));
        }
        if (active.HasValue) q = q.Where(s => s.IsActive == active.Value);

        var total = await q.CountAsync(ct);

        var items = await q
            .OrderByDescending(s => s.LoginAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(s => new LoginSessionDto
            {
                Id = s.Id,
                UserId = s.UserId,
                Email = s.Email,
                UserName = s.UserName,
                Role = s.Role,
                TenantId = s.TenantId,
                AuthMethod = s.AuthMethod,
                Ip = s.Ip,
                Device = s.Device,
                GeoCountry = s.GeoCountry,
                GeoRegion = s.GeoRegion,
                GeoCity = s.GeoCity,
                Latitude = s.Latitude,
                Longitude = s.Longitude,
                Accuracy = s.Accuracy,
                GeoConsent = s.GeoConsent,
                GeoConsentAt = s.GeoConsentAt,
                LoginAt = s.LoginAt,
                LastSeenAt = s.LastSeenAt,
                ActiveSeconds = s.ActiveSeconds,
                ActiveMinutes = (int)Math.Round(s.ActiveSeconds / 60.0),
                EndedAt = s.EndedAt,
                EndReason = s.EndReason,
                IsActive = s.IsActive,
            })
            .ToListAsync(ct);

        return new LoginSessionPageDto { Items = items, Total = total, Page = page, PageSize = pageSize };
    }

    public async Task<EntityChangePageDto> QueryEntityChangesAsync(
        DateTime? from, DateTime? to, string? entityType, string? entityId, int? userId,
        int page, int pageSize, CancellationToken ct = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 50;
        if (pageSize > 500) pageSize = 500;

        var q = _db.EntityChangeLogs.AsNoTracking().AsQueryable();

        if (from.HasValue) q = q.Where(e => e.CreatedAt >= from.Value);
        if (to.HasValue) q = q.Where(e => e.CreatedAt <= to.Value);
        if (!string.IsNullOrWhiteSpace(entityType))
        {
            var t = entityType.Trim();
            q = q.Where(e => e.EntityType == t);
        }
        if (!string.IsNullOrWhiteSpace(entityId))
        {
            var id = entityId.Trim();
            q = q.Where(e => e.EntityId == id);
        }
        if (userId.HasValue) q = q.Where(e => e.UserId == userId.Value);

        var total = await q.CountAsync(ct);

        var items = await q
            .OrderByDescending(e => e.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(e => new EntityChangeDto
            {
                Id = e.Id,
                UserId = e.UserId,
                Email = e.Email,
                Role = e.Role,
                TenantId = e.TenantId,
                EntityType = e.EntityType,
                EntityId = e.EntityId,
                Action = e.Action,
                ChangesJson = e.ChangesJson,
                CreatedAt = e.CreatedAt,
            })
            .ToListAsync(ct);

        return new EntityChangePageDto { Items = items, Total = total, Page = page, PageSize = pageSize };
    }

    /// <summary>Usuários que ativaram a localização + a posição mais recente.</summary>
    public async Task<List<LocationConsentDto>> LocationConsentsAsync(CancellationToken ct = default)
    {
        var users = await _db.Users.AsNoTracking()
            .Where(u => u.LocationConsent)
            .Select(u => new { u.Id, u.Name, u.Email, u.Role, u.LocationConsentAt })
            .ToListAsync(ct);

        var result = new List<LocationConsentDto>(users.Count);
        foreach (var u in users)
        {
            var last = await _db.LoginSessions.AsNoTracking()
                .Where(s => s.UserId == u.Id && s.GeoConsent)
                .OrderByDescending(s => s.GeoConsentAt)
                .Select(s => new { s.Latitude, s.Longitude, s.Accuracy, s.LastSeenAt, s.GeoCity })
                .FirstOrDefaultAsync(ct);

            result.Add(new LocationConsentDto
            {
                UserId = u.Id,
                Name = u.Name,
                Email = u.Email,
                Role = u.Role,
                ConsentAt = u.LocationConsentAt,
                Latitude = last?.Latitude,
                Longitude = last?.Longitude,
                Accuracy = last?.Accuracy,
                LastSeenAt = last?.LastSeenAt,
                GeoCity = last?.GeoCity,
            });
        }

        return result;
    }
}
