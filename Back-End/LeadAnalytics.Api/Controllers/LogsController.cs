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

    public record LogsLoginDto(string Username, string Password);

    /// <summary>
    /// Login do painel de logs (usuário/senha vêm da seção LogsAuth do appsettings).
    /// Retorna um token opaco que deve ser enviado no header X-Logs-Token.
    /// </summary>
    [HttpPost("auth")]
    public IActionResult Login([FromBody] LogsLoginDto dto)
    {
        if (dto is null)
            return BadRequest(new { message = "Credenciais obrigatórias." });

        var result = _auth.TryLogin(dto.Username, dto.Password);
        if (result is null)
            return Unauthorized(new { message = "Usuário ou senha inválidos." });

        var (token, expiresAt) = result.Value;
        return Ok(new
        {
            token,
            expiresAt,
            sessionTtlMinutes = _auth.SessionTtlMinutes,
        });
    }

    /// <summary>Encerra a sessão.</summary>
    [HttpPost("logout")]
    [LogsTokenAuthorize]
    public IActionResult Logout()
    {
        var token = Request.Headers["X-Logs-Token"].ToString();
        if (string.IsNullOrEmpty(token))
        {
            var auth = Request.Headers.Authorization.ToString();
            if (auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                token = auth["Bearer ".Length..].Trim();
        }
        _auth.Revoke(token);
        return NoContent();
    }

    /// <summary>Lista os logs em memória, filtrando por nível, texto e janela de tempo.</summary>
    [HttpGet]
    [LogsTokenAuthorize]
    public IActionResult List(
        [FromQuery] string? level = null,
        [FromQuery] string? search = null,
        [FromQuery] int? sinceMinutes = null,
        [FromQuery] int limit = 500)
    {
        DateTime? since = sinceMinutes is > 0
            ? DateTime.UtcNow.AddMinutes(-sinceMinutes.Value)
            : null;

        limit = Math.Clamp(limit, 1, 2000);

        var items = _store.Query(level, search, since, limit);

        return Ok(new
        {
            total = _store.Count,
            returned = items.Count,
            items = items.Select(e => new
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
            })
        });
    }

    /// <summary>Contadores por nível.</summary>
    [HttpGet("stats")]
    [LogsTokenAuthorize]
    public IActionResult Stats([FromQuery] int? sinceMinutes = null)
    {
        DateTime? since = sinceMinutes is > 0
            ? DateTime.UtcNow.AddMinutes(-sinceMinutes.Value)
            : null;

        return Ok(new
        {
            total = _store.Count,
            byLevel = _store.Stats(since),
        });
    }

    /// <summary>Esvazia o buffer em memória.</summary>
    [HttpDelete]
    [LogsTokenAuthorize]
    public IActionResult Clear()
    {
        _store.Clear();
        return NoContent();
    }
}
