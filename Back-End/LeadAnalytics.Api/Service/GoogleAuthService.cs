using System.Text.Json.Serialization;

namespace LeadAnalytics.Api.Service;

public record GoogleUserInfo(string Email, string Name, string Sub, string? Picture);

public class GoogleAuthService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _config;
    private readonly ILogger<GoogleAuthService> _logger;

    public GoogleAuthService(
        HttpClient httpClient,
        IConfiguration config,
        ILogger<GoogleAuthService> logger)
    {
        _httpClient = httpClient;
        _config = config;
        _logger = logger;
    }

    private string? GetClientId()
    {
        // Lemos APENAS de variável de ambiente (decisão do produto).
        // ASP.NET Core já mapeia env vars para IConfiguration, então
        // GOOGLE_CLIENT_ID aparece como _config["GOOGLE_CLIENT_ID"].
        return Environment.GetEnvironmentVariable("GOOGLE_CLIENT_ID")
            ?? _config["GOOGLE_CLIENT_ID"];
    }

    public async Task<GoogleUserInfo?> ValidateIdTokenAsync(string idToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(idToken))
        {
            _logger.LogWarning("Google idToken vazio");
            return null;
        }

        var expectedAud = GetClientId();
        if (string.IsNullOrWhiteSpace(expectedAud))
        {
            _logger.LogError("GOOGLE_CLIENT_ID não configurado no ambiente");
            return null;
        }

        try
        {
            var url = $"https://oauth2.googleapis.com/tokeninfo?id_token={Uri.EscapeDataString(idToken)}";
            using var resp = await _httpClient.GetAsync(url, ct);

            if (!resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync(ct);
                _logger.LogWarning("Google tokeninfo falhou: {Status} {Body}", resp.StatusCode, body);
                return null;
            }

            var info = await resp.Content.ReadFromJsonAsync<GoogleTokenInfo>(cancellationToken: ct);
            if (info is null)
            {
                _logger.LogWarning("Google tokeninfo retornou null");
                return null;
            }

            if (!string.Equals(info.Aud, expectedAud, StringComparison.Ordinal))
            {
                _logger.LogWarning("Google audience mismatch: got={Got}", info.Aud);
                return null;
            }

            if (!string.Equals(info.EmailVerified, "true", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Google email não verificado: {Email}", info.Email);
                return null;
            }

            if (string.IsNullOrWhiteSpace(info.Email) || string.IsNullOrWhiteSpace(info.Sub))
            {
                _logger.LogWarning("Google tokeninfo sem email/sub");
                return null;
            }

            // Issuer check - aceita ambos os formatos do Google
            if (!string.IsNullOrWhiteSpace(info.Iss) &&
                info.Iss != "accounts.google.com" &&
                info.Iss != "https://accounts.google.com")
            {
                _logger.LogWarning("Google issuer inesperado: {Iss}", info.Iss);
                return null;
            }

            // Expiração
            if (long.TryParse(info.Exp, out var exp))
            {
                var expUtc = DateTimeOffset.FromUnixTimeSeconds(exp).UtcDateTime;
                if (expUtc < DateTime.UtcNow)
                {
                    _logger.LogWarning("Google idToken expirado: {Exp}", expUtc);
                    return null;
                }
            }

            return new GoogleUserInfo(
                Email: info.Email.Trim().ToLowerInvariant(),
                Name: info.Name ?? info.Email,
                Sub: info.Sub,
                Picture: info.Picture
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao validar Google idToken");
            return null;
        }
    }

    private class GoogleTokenInfo
    {
        [JsonPropertyName("iss")] public string? Iss { get; set; }
        [JsonPropertyName("aud")] public string? Aud { get; set; }
        [JsonPropertyName("sub")] public string? Sub { get; set; }
        [JsonPropertyName("email")] public string? Email { get; set; }
        [JsonPropertyName("email_verified")] public string? EmailVerified { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("picture")] public string? Picture { get; set; }
        [JsonPropertyName("exp")] public string? Exp { get; set; }
    }
}
