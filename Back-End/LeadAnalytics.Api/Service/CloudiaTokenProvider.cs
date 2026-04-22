using System.IdentityModel.Tokens.Jwt;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LeadAnalytics.Api.Service;

public record CloudiaLoginRequest(string Email, string Password);

public record CloudiaClinic(
    [property: JsonPropertyName("idclinic")] int IdClinic,
    [property: JsonPropertyName("name")] string Name);

public record CloudiaLoginResponse(
    [property: JsonPropertyName("clinics")] List<CloudiaClinic> Clinics,
    [property: JsonPropertyName("token")] string Token);

public class CloudiaTokenProvider
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<CloudiaTokenProvider> _logger;
    private readonly string _baseUrl = "https://api-prd.cloudiabot.com";

    private string? _cachedToken;
    private DateTime _tokenExpiresAt = DateTime.MinValue;
    private readonly object _lock = new();
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public CloudiaTokenProvider(
        IHttpClientFactory httpClientFactory,
        IConfiguration config,
        ILogger<CloudiaTokenProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _config = config;
        _logger = logger;
    }

    public async Task<string?> GetTokenAsync()
    {
        lock (_lock)
        {
            // Se token está válido e não vai expirar nos próximos 6 horas, retorna cache
            if (!string.IsNullOrWhiteSpace(_cachedToken) && DateTime.UtcNow.AddHours(6) < _tokenExpiresAt)
            {
                _logger.LogDebug("Usando token Cloudia em cache (expira em {ExpiresAt})", _tokenExpiresAt);
                return _cachedToken;
            }
        }

        // Fora do lock: chama login (pode demorar)
        return await LoginAsync();
    }

    /// <summary>Invalida o cache e força renovação do token (usado em caso de 401).</summary>
    public void InvalidateCache()
    {
        lock (_lock)
        {
            _cachedToken = null;
            _tokenExpiresAt = DateTime.MinValue;
            _logger.LogWarning("Cache de token Cloudia invalidado (recebido 401)");
        }
    }

    private async Task<string?> LoginAsync()
    {
        var email = _config["Cloudia:Email"] ?? Environment.GetEnvironmentVariable("CLOUDIA_EMAIL");
        var password = _config["Cloudia:Password"] ?? Environment.GetEnvironmentVariable("CLOUDIA_PASSWORD");

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            _logger.LogWarning("Credenciais Cloudia não configuradas (Cloudia:Email/Password ou env vars)");
            return null;
        }

        try
        {
            var httpClient = _httpClientFactory.CreateClient();
            var loginUrl = $"{_baseUrl}/api/auth/token";
            var payload = new CloudiaLoginRequest(email, password);
            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

            _logger.LogInformation("Autenticando contra Cloudia em {Url} com email {Email}", loginUrl, email);
            var response = await httpClient.PostAsync(loginUrl, content);

            var responseContent = await response.Content.ReadAsStringAsync();
            _logger.LogInformation("Resposta Cloudia login: Status={Status}, Body={Body}", response.StatusCode, responseContent);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Falha ao autenticar Cloudia: {Status} {Content}", response.StatusCode, responseContent);
                return null;
            }

            var loginResponse = JsonSerializer.Deserialize<CloudiaLoginResponse>(responseContent, JsonOptions);

            if (string.IsNullOrWhiteSpace(loginResponse?.Token))
            {
                _logger.LogWarning("Resposta de login Cloudia não contém token. Response: {Response}", responseContent);
                return null;
            }

            // Decodifica JWT pra ler expiry (sem validar assinatura, só o payload)
            var handler = new JwtSecurityTokenHandler();
            var token = handler.ReadJwtToken(loginResponse.Token);
            var expiresAt = token.ValidTo;

            lock (_lock)
            {
                _cachedToken = loginResponse.Token;
                _tokenExpiresAt = expiresAt;
            }

            _logger.LogInformation("Token Cloudia renovado com sucesso (expira em {ExpiresAt})", expiresAt);

            return loginResponse.Token;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao autenticar Cloudia");
            return null;
        }
    }
}
