using System.Security.Claims;
using LeadAnalytics.Api.Service;

namespace LeadAnalytics.Api.Middleware;

/// <summary>
/// Barreira de verdade para papéis somente-leitura (ex.: <c>trafego_pago</c>): bloqueia
/// métodos HTTP que mutam estado (POST/PUT/PATCH/DELETE) com 403, mesmo que o front
/// permita o clique. Mantém uma whitelist para o que é essencial à própria sessão
/// (login/logout/heartbeat/consentimento de localização). Roda depois da
/// autenticação e antes do <see cref="AuditLogMiddleware"/>, então tentativas
/// bloqueadas continuam sendo auditadas.
/// </summary>
public class ReadOnlyRoleMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ReadOnlyRoleMiddleware> _logger;

    private static readonly HashSet<string> SafeMethods =
        new(StringComparer.OrdinalIgnoreCase) { "GET", "HEAD", "OPTIONS", "TRACE" };

    public ReadOnlyRoleMiddleware(RequestDelegate next, ILogger<ReadOnlyRoleMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext ctx)
    {
        var method = ctx.Request.Method;

        if (!SafeMethods.Contains(method))
        {
            var role = ctx.User?.FindFirstValue(ClaimTypes.Role);
            if (Roles.IsReadOnly(role))
            {
                var path = ctx.Request.Path.Value ?? string.Empty;

                // Whitelist: ações ligadas à própria sessão do usuário.
                var allowed =
                    path.StartsWith("/api/auth", StringComparison.OrdinalIgnoreCase);

                if (!allowed)
                {
                    _logger.LogInformation(
                        "🚫 Read-only ({Role}) bloqueado: {Method} {Path}", role, method, path);
                    ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                    await ctx.Response.WriteAsJsonAsync(new
                    {
                        message = "Seu perfil é somente leitura e não pode alterar dados."
                    });
                    return;
                }
            }
        }

        await _next(ctx);
    }
}
