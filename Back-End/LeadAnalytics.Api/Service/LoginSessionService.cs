using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace LeadAnalytics.Api.Service;

/// <summary>
/// Gerencia as sessões de login: cria no login (com IP/UA + GeoIP em background),
/// acumula tempo ativo via heartbeat, grava o consentimento/coordenadas de GPS e
/// encerra a sessão. Veja <see cref="LoginSession"/>.
/// </summary>
public class LoginSessionService
{
    private readonly AppDbContext _db;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<LoginSessionService> _logger;

    /// <summary>Teto por heartbeat para não inflar o tempo ativo (front pinga ~60s).</summary>
    private const long MaxBeatSeconds = 120;

    public LoginSessionService(
        AppDbContext db,
        IServiceScopeFactory scopeFactory,
        ILogger<LoginSessionService> logger)
    {
        _db = db;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <summary>Cria a sessão no login e dispara o GeoIP em background. Retorna o Id.</summary>
    public async Task<long> StartAsync(
        User user, string authMethod, string? ip, string? userAgent, CancellationToken ct = default)
    {
        var session = new LoginSession
        {
            UserId = user.Id,
            Email = user.Email,
            UserName = user.Name,
            Role = user.Role,
            TenantId = user.TenantId,
            AuthMethod = authMethod,
            Ip = Truncate(ip, 64),
            UserAgent = Truncate(userAgent, 400),
            Device = ParseDevice(userAgent),
            LoginAt = DateTime.UtcNow,
            LastSeenAt = DateTime.UtcNow,
            IsActive = true,
        };

        _db.LoginSessions.Add(session);
        await _db.SaveChangesAsync(ct);

        // GeoIP fora do caminho crítico do login.
        var sessionId = session.Id;
        var ipCopy = session.Ip;
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var geo = scope.ServiceProvider.GetRequiredService<GeoIpService>();
                var result = await geo.LookupAsync(ipCopy);
                if (result is null) return;

                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var s = await db.LoginSessions.FirstOrDefaultAsync(x => x.Id == sessionId);
                if (s is null) return;
                s.GeoCountry = Truncate(result.Country, 80);
                s.GeoRegion = Truncate(result.Region, 80);
                s.GeoCity = Truncate(result.City, 120);
                await db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Falha ao preencher GeoIP da sessão {SessionId}", sessionId);
            }
        });

        return sessionId;
    }

    /// <summary>Heartbeat: soma tempo ativo (com teto) e atualiza o último visto.</summary>
    public async Task HeartbeatAsync(long sessionId, int userId, CancellationToken ct = default)
    {
        var s = await _db.LoginSessions
            .FirstOrDefaultAsync(x => x.Id == sessionId && x.UserId == userId, ct);
        if (s is null || !s.IsActive) return;

        var now = DateTime.UtcNow;
        var delta = (long)Math.Round((now - s.LastSeenAt).TotalSeconds);
        if (delta > 0)
            s.ActiveSeconds += Math.Min(delta, MaxBeatSeconds);
        s.LastSeenAt = now;
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>Grava o consentimento + coordenadas de GPS na sessão e marca o usuário.</summary>
    public async Task<bool> SetGeoConsentAsync(
        long sessionId, int userId, double latitude, double longitude, double? accuracy,
        CancellationToken ct = default)
    {
        var s = await _db.LoginSessions
            .FirstOrDefaultAsync(x => x.Id == sessionId && x.UserId == userId, ct);
        if (s is null) return false;

        var now = DateTime.UtcNow;
        s.Latitude = latitude;
        s.Longitude = longitude;
        s.Accuracy = accuracy;
        s.GeoConsent = true;
        s.GeoConsentAt = now;

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is not null)
        {
            user.LocationConsent = true;
            user.LocationConsentAt = now;
            user.UpdatedAt = now;
        }

        await _db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>Encerra a sessão (logout / expiração).</summary>
    public async Task EndAsync(long sessionId, int userId, string reason, CancellationToken ct = default)
    {
        var s = await _db.LoginSessions
            .FirstOrDefaultAsync(x => x.Id == sessionId && x.UserId == userId, ct);
        if (s is null || !s.IsActive) return;

        s.IsActive = false;
        s.EndedAt = DateTime.UtcNow;
        s.EndReason = Truncate(reason, 40);
        await _db.SaveChangesAsync(ct);
    }

    private static string? ParseDevice(string? ua)
    {
        if (string.IsNullOrWhiteSpace(ua)) return null;
        var u = ua.ToLowerInvariant();

        string os =
            u.Contains("windows") ? "Windows" :
            u.Contains("iphone") || u.Contains("ipad") || u.Contains("ios") ? "iOS" :
            u.Contains("android") ? "Android" :
            u.Contains("mac os") || u.Contains("macintosh") ? "macOS" :
            u.Contains("linux") ? "Linux" : "Outro";

        string browser =
            u.Contains("edg/") || u.Contains("edge") ? "Edge" :
            u.Contains("opr/") || u.Contains("opera") ? "Opera" :
            u.Contains("chrome") ? "Chrome" :
            u.Contains("firefox") ? "Firefox" :
            u.Contains("safari") ? "Safari" : "Outro";

        return Truncate($"{browser} · {os}", 200);
    }

    private static string? Truncate(string? value, int max)
    {
        if (string.IsNullOrEmpty(value)) return value;
        return value.Length <= max ? value : value[..max];
    }
}
