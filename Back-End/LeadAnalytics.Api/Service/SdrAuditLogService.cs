using System.Text.Json;
using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.DTOs.Sdr;
using LeadAnalytics.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace LeadAnalytics.Api.Service;

/// <summary>
/// Serviço central de auditoria do fluxo SDR.
/// Toda mudança importante (criar, editar, aprovar, rejeitar, deletar) deve passar por aqui.
/// A chefe consulta via /api/sdr/auditoria para saber quem fez o quê e quando.
/// </summary>
public class SdrAuditLogService(
    AppDbContext db,
    IHttpContextAccessor httpAccessor,
    ICurrentUser currentUser,
    ILogger<SdrAuditLogService> logger)
{
    private readonly AppDbContext _db = db;
    private readonly IHttpContextAccessor _http = httpAccessor;
    private readonly ICurrentUser _currentUser = currentUser;
    private readonly ILogger<SdrAuditLogService> _logger = logger;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Persiste uma entrada de auditoria. Não joga exceção em caso de falha — apenas loga, pois
    /// auditoria não deve quebrar o fluxo principal.
    /// </summary>
    public async Task RecordAsync(
        int tenantId,
        string action,
        string entityType,
        int entityId,
        string summary,
        object? before = null,
        object? after = null,
        CancellationToken ct = default)
    {
        try
        {
            var (userName, userEmail) = await ResolveUserSnapshotAsync(ct);
            var (ip, ua) = ResolveRequestMeta();

            var log = new SdrAuditLog
            {
                TenantId = tenantId,
                UserId = _currentUser.UserId,
                UserName = userName,
                UserEmail = userEmail,
                Action = action,
                EntityType = entityType,
                EntityId = entityId,
                Summary = summary.Length > 500 ? summary[..500] : summary,
                BeforeJson = before is null ? null : JsonSerializer.Serialize(before, JsonOpts),
                AfterJson = after is null ? null : JsonSerializer.Serialize(after, JsonOpts),
                IpAddress = ip,
                UserAgent = ua?.Length > 500 ? ua[..500] : ua,
                CreatedAt = DateTime.UtcNow,
            };

            _db.SdrAuditLogs.Add(log);
            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Falha ao gravar audit log: tenant={Tenant} action={Action} entityType={EntityType} entityId={EntityId}",
                tenantId, action, entityType, entityId);
        }
    }

    public async Task<List<SdrAuditLogResponseDto>> ListAsync(
        int tenantId,
        DateTime? from = null,
        DateTime? to = null,
        string? entityType = null,
        int? entityId = null,
        string? action = null,
        int? userId = null,
        int page = 1,
        int pageSize = 50,
        CancellationToken ct = default)
    {
        var query = _db.SdrAuditLogs
            .AsNoTracking()
            .Where(l => l.TenantId == tenantId);

        if (from.HasValue) query = query.Where(l => l.CreatedAt >= from);
        if (to.HasValue) query = query.Where(l => l.CreatedAt <= to);
        if (!string.IsNullOrWhiteSpace(entityType)) query = query.Where(l => l.EntityType == entityType);
        if (entityId.HasValue) query = query.Where(l => l.EntityId == entityId);
        if (!string.IsNullOrWhiteSpace(action)) query = query.Where(l => l.Action == action);
        if (userId.HasValue) query = query.Where(l => l.UserId == userId);

        if (page < 1) page = 1;
        if (pageSize < 1 || pageSize > 200) pageSize = 50;

        var rows = await query
            .OrderByDescending(l => l.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return rows.Select(MapToDto).ToList();
    }

    public async Task<int> CountAsync(int tenantId, CancellationToken ct = default) =>
        await _db.SdrAuditLogs.AsNoTracking().CountAsync(l => l.TenantId == tenantId, ct);

    private async Task<(string? Name, string? Email)> ResolveUserSnapshotAsync(CancellationToken ct)
    {
        var userId = _currentUser.UserId;
        if (userId is null) return (null, null);
        var user = await _db.Users.AsNoTracking()
            .Where(u => u.Id == userId.Value)
            .Select(u => new { u.Name, u.Email })
            .FirstOrDefaultAsync(ct);
        return user is null ? (null, null) : (user.Name, user.Email);
    }

    private (string? Ip, string? Ua) ResolveRequestMeta()
    {
        var ctx = _http.HttpContext;
        if (ctx is null) return (null, null);
        var ip = ctx.Connection.RemoteIpAddress?.ToString();
        var ua = ctx.Request.Headers.UserAgent.ToString();
        if (string.IsNullOrEmpty(ua)) ua = null;
        return (ip, ua);
    }

    private static SdrAuditLogResponseDto MapToDto(SdrAuditLog l) => new()
    {
        Id = l.Id,
        UserId = l.UserId,
        UserName = l.UserName,
        UserEmail = l.UserEmail,
        Action = l.Action,
        EntityType = l.EntityType,
        EntityId = l.EntityId,
        Summary = l.Summary,
        BeforeJson = l.BeforeJson,
        AfterJson = l.AfterJson,
        IpAddress = l.IpAddress,
        UserAgent = l.UserAgent,
        CreatedAt = l.CreatedAt,
    };
}
