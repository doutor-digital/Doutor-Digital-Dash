using System.Text.Json;
using System.Threading.Channels;
using LeadAnalytics.Api.Service;
using Microsoft.AspNetCore.Mvc;

namespace LeadAnalytics.Api.Controllers;

[ApiController]
[Route("logs")]
public class LogsController(
    InMemoryLogStore store,
    LogsAuthService auth) : ControllerBase
{
    private readonly InMemoryLogStore _store = store;
    private readonly LogsAuthService _auth = auth;

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public record LogsLoginDto(string Username, string Password);

    /// <summary>Login do painel de logs (usuário/senha vêm do appsettings:LogsAuth).</summary>
    [HttpPost("auth")]
    public IActionResult Login([FromBody] LogsLoginDto dto)
    {
        if (dto is null)
            return BadRequest(new { message = "Credenciais obrigatórias." });

        var result = _auth.TryLogin(dto.Username, dto.Password);
        if (result is null)
            return Unauthorized(new { message = "Usuário ou senha inválidos." });

        var (token, expiresAt) = result.Value;
        return Ok(new { token, expiresAt, sessionTtlMinutes = _auth.SessionTtlMinutes });
    }

    [HttpPost("logout")]
    [LogsTokenAuthorize]
    public IActionResult Logout()
    {
        _auth.Revoke(ExtractToken());
        return NoContent();
    }

    [HttpGet]
    [LogsTokenAuthorize]
    public IActionResult List(
        [FromQuery] string? level = null,
        [FromQuery] string? search = null,
        [FromQuery] int? sinceMinutes = null,
        [FromQuery] int limit = 500)
    {
        DateTime? since = sinceMinutes is > 0 ? DateTime.UtcNow.AddMinutes(-sinceMinutes.Value) : null;
        limit = Math.Clamp(limit, 1, 2000);

        var items = _store.Query(level, search, since, limit);

        return Ok(new
        {
            total = _store.Count,
            returned = items.Count,
            items = items.Select(ToDto),
        });
    }

    [HttpGet("stats")]
    [LogsTokenAuthorize]
    public IActionResult Stats([FromQuery] int? sinceMinutes = null)
    {
        DateTime? since = sinceMinutes is > 0 ? DateTime.UtcNow.AddMinutes(-sinceMinutes.Value) : null;
        return Ok(new { total = _store.Count, byLevel = _store.Stats(since) });
    }

    [HttpDelete]
    [LogsTokenAuthorize]
    public IActionResult Clear()
    {
        _store.Clear();
        return NoContent();
    }

    /// <summary>
    /// Server-Sent Events stream — recebe cada log novo em tempo real.
    /// Autenticação via query string ?token=... (EventSource não permite headers).
    /// </summary>
    [HttpGet("stream")]
    public async Task Stream(
        [FromQuery] string token,
        [FromQuery] string? level = null,
        [FromQuery] string? search = null,
        CancellationToken ct = default)
    {
        if (!_auth.Validate(token))
        {
            Response.StatusCode = 401;
            await Response.WriteAsync("unauthorized", ct);
            return;
        }

        Response.Headers.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache, no-store";
        Response.Headers.Connection = "keep-alive";
        Response.Headers["X-Accel-Buffering"] = "no"; // desliga buffering em reverse proxies

        var channel = Channel.CreateBounded<LogEntry>(new BoundedChannelOptions(1024)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
        });

        void Handler(LogEntry entry)
        {
            if (!Matches(entry, level, search)) return;
            channel.Writer.TryWrite(entry);
        }

        _store.EntryAdded += Handler;

        try
        {
            await WriteSseAsync("hello", new { ok = true, bufferSize = _store.Count }, ct);

            // Heartbeat pra manter a conexão viva em redes com idle timeout
            var heartbeat = Task.Run(async () =>
            {
                while (!ct.IsCancellationRequested)
                {
                    await Task.Delay(TimeSpan.FromSeconds(15), ct);
                    try
                    {
                        await Response.WriteAsync(": ping\n\n", ct);
                        await Response.Body.FlushAsync(ct);
                    }
                    catch { break; }
                }
            }, ct);

            await foreach (var entry in channel.Reader.ReadAllAsync(ct))
            {
                await WriteSseAsync("log", ToDto(entry), ct);
            }
        }
        catch (OperationCanceledException) { /* cliente desconectou */ }
        finally
        {
            _store.EntryAdded -= Handler;
            channel.Writer.TryComplete();
        }
    }

    /// <summary>Serve o painel HTML estático (dashboard autônomo do backend).</summary>
    [HttpGet("~/admin")]
    [HttpGet("~/admin/")]
    public IActionResult AdminRedirect() => Redirect("/admin/index.html");

    // ─── helpers ────────────────────────────────────────────────

    private async Task WriteSseAsync(string evt, object data, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(data, Json);
        await Response.WriteAsync($"event: {evt}\n", ct);
        await Response.WriteAsync($"data: {json}\n\n", ct);
        await Response.Body.FlushAsync(ct);
    }

    private static bool Matches(LogEntry e, string? level, string? search)
    {
        if (!string.IsNullOrWhiteSpace(level))
        {
            var levels = level
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (!levels.Contains(e.Level)) return false;
        }
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            var hit = e.Message.Contains(s, StringComparison.OrdinalIgnoreCase)
                   || e.Category.Contains(s, StringComparison.OrdinalIgnoreCase)
                   || (e.Exception?.Contains(s, StringComparison.OrdinalIgnoreCase) ?? false)
                   || (e.Path?.Contains(s, StringComparison.OrdinalIgnoreCase) ?? false);
            if (!hit) return false;
        }
        return true;
    }

    private string? ExtractToken()
    {
        var t = Request.Headers["X-Logs-Token"].ToString();
        if (!string.IsNullOrWhiteSpace(t)) return t;
        var auth = Request.Headers.Authorization.ToString();
        return auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? auth["Bearer ".Length..].Trim()
            : null;
    }

    private static object ToDto(LogEntry e) => new
    {
        e.Id,
        e.Timestamp,
        e.Level,
        e.Category,
        e.Message,
        e.Exception,
        e.Path,
        e.Method,
        e.TraceId,
    };
}
