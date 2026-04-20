using System.Collections.Concurrent;
using System.Security.Cryptography;
using LeadAnalytics.Api.Options;
using Microsoft.Extensions.Options;

namespace LeadAnalytics.Api.Service;

/// <summary>
/// Autenticação independente para o painel de logs.
/// As credenciais vêm do appsettings.json (seção LogsAuth) ou env vars
/// (LogsAuth__Username, LogsAuth__Password). Emite um token opaco
/// em memória com TTL deslizante.
/// </summary>
public class LogsAuthService
{
    private readonly IOptionsMonitor<LogsAuthOptions> _options;
    private readonly ConcurrentDictionary<string, DateTime> _tokens = new();
    private readonly ILogger<LogsAuthService> _logger;

    public LogsAuthService(
        IOptionsMonitor<LogsAuthOptions> options,
        ILogger<LogsAuthService> logger)
    {
        _options = options;
        _logger = logger;
    }

    public int SessionTtlMinutes => Math.Max(1, _options.CurrentValue.SessionTtlMinutes);

    /// <summary>Verifica credenciais e emite token. Retorna null se inválido.</summary>
    public (string token, DateTime expiresAt)? TryLogin(string? username, string? password)
    {
        var opt = _options.CurrentValue;
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            return null;

        var okUser = StringComparer.OrdinalIgnoreCase.Equals(username.Trim(), opt.Username);
        var okPass = FixedTimeEquals(password, opt.Password);

        if (!okUser || !okPass)
        {
            _logger.LogWarning("🔒 Tentativa inválida de login no painel de logs ({User}).", username);
            return null;
        }

        var token = GenerateToken();
        var expiresAt = DateTime.UtcNow.AddMinutes(SessionTtlMinutes);
        _tokens[token] = expiresAt;

        CleanupExpired();
        return (token, expiresAt);
    }

    /// <summary>Valida o token e estende o TTL (sliding).</summary>
    public bool Validate(string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return false;
        if (!_tokens.TryGetValue(token, out var expiresAt)) return false;

        if (expiresAt <= DateTime.UtcNow)
        {
            _tokens.TryRemove(token, out _);
            return false;
        }

        // Sliding expiration — cada uso renova.
        _tokens[token] = DateTime.UtcNow.AddMinutes(SessionTtlMinutes);
        return true;
    }

    public void Revoke(string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return;
        _tokens.TryRemove(token, out _);
    }

    private static string GenerateToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes)
            .Replace("+", "-").Replace("/", "_").TrimEnd('=');
    }

    private void CleanupExpired()
    {
        var now = DateTime.UtcNow;
        foreach (var kv in _tokens)
        {
            if (kv.Value <= now) _tokens.TryRemove(kv.Key, out _);
        }
    }

    private static bool FixedTimeEquals(string a, string b)
    {
        var ab = System.Text.Encoding.UTF8.GetBytes(a);
        var bb = System.Text.Encoding.UTF8.GetBytes(b);
        if (ab.Length != bb.Length) return false;
        return CryptographicOperations.FixedTimeEquals(ab, bb);
    }
}
