using System.Diagnostics;
using System.Security.Claims;
using LeadAnalytics.Api.Models;
using LeadAnalytics.Api.Service;

namespace LeadAnalytics.Api.Middleware;

public class AuditLogMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IServiceProvider _rootProvider;
    private readonly ILogger<AuditLogMiddleware> _logger;

    public AuditLogMiddleware(
        RequestDelegate next,
        IServiceProvider rootProvider,
        ILogger<AuditLogMiddleware> logger)
    {
        _next = next;
        _rootProvider = rootProvider;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext ctx)
    {
        var path = ctx.Request.Path.Value ?? string.Empty;

        // Pula rotas que polulariam demais o log
        var skip = path.StartsWith("/swagger", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/api/audit-logs", StringComparison.OrdinalIgnoreCase)
            || path.Equals("/health", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/static", StringComparison.OrdinalIgnoreCase);

        if (skip)
        {
            await _next(ctx);
            return;
        }

        var sw = Stopwatch.StartNew();
        Exception? error = null;

        try
        {
            await _next(ctx);
        }
        catch (Exception ex)
        {
            error = ex;
            throw;
        }
        finally
        {
            sw.Stop();

            // Decisão do produto: tudo autenticado é registrado.
            // Adicionalmente, registramos auth/invitations mesmo sem autenticação
            // para capturar tentativas de login/convite.
            var isAuth = ctx.User?.Identity?.IsAuthenticated ?? false;
            var isAuthEndpoint = path.StartsWith("/api/auth", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("/api/invitations", StringComparison.OrdinalIgnoreCase);

            if (isAuth || isAuthEndpoint)
            {
                var snap = new AuditLog
                {
                    UserId = TryGetClaimInt(ctx.User, ClaimTypes.NameIdentifier),
                    Email = ctx.User?.FindFirstValue(ClaimTypes.Email),
                    UserName = ctx.User?.FindFirstValue(ClaimTypes.Name),
                    Role = ctx.User?.FindFirstValue(ClaimTypes.Role),
                    TenantId = TryGetClaimInt(ctx.User, "tenant_id"),
                    AuthMethod = ctx.User?.FindFirst("auth_method")?.Value,
                    Ip = ResolveIp(ctx),
                    UserAgent = Truncate(ctx.Request.Headers["User-Agent"].ToString(), 400),
                    Method = ctx.Request.Method,
                    Path = Truncate(path, 400)!,
                    QueryString = Truncate(ctx.Request.QueryString.Value, 1000),
                    StatusCode = error == null ? ctx.Response.StatusCode : 500,
                    DurationMs = (int)sw.ElapsedMilliseconds,
                    CreatedAt = DateTime.UtcNow
                };

                // Fire-and-forget para não atrasar a resposta
                _ = Task.Run(async () =>
                {
                    try
                    {
                        using var scope = _rootProvider.CreateScope();
                        var svc = scope.ServiceProvider.GetRequiredService<AuditLogService>();
                        await svc.WriteAsync(snap);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Falha ao gravar audit log");
                    }
                });
            }
        }
    }

    private static int? TryGetClaimInt(ClaimsPrincipal? user, string claim)
    {
        var v = user?.FindFirst(claim)?.Value;
        return int.TryParse(v, out var n) ? n : null;
    }

    private static string? ResolveIp(HttpContext ctx)
    {
        var fwd = ctx.Request.Headers["X-Forwarded-For"].ToString();
        if (!string.IsNullOrWhiteSpace(fwd))
        {
            var first = fwd.Split(',')[0].Trim();
            if (!string.IsNullOrWhiteSpace(first)) return Truncate(first, 64);
        }
        return Truncate(ctx.Connection.RemoteIpAddress?.ToString(), 64);
    }

    private static string? Truncate(string? value, int max)
    {
        if (string.IsNullOrEmpty(value)) return value;
        return value.Length <= max ? value : value[..max];
    }
}
