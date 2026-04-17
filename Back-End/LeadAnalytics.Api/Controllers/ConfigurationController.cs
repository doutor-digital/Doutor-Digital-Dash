using LeadAnalytics.Api.Service;
using Microsoft.AspNetCore.Mvc;

namespace LeadAnalytics.Api.Controllers;

[ApiController]
[Route("api/config")]
public class ConfigurationController(
    ConfigurationService configService,
    ILogger<ConfigurationController> logger,
    IConfiguration configuration) : ControllerBase
{
    private readonly ConfigurationService _configService = configService;
    private readonly ILogger<ConfigurationController> _logger = logger;
    private readonly IConfiguration _configuration = configuration;

    private async Task<bool> IsAdminAuthorizedAsync(string? adminKey)
    {
        var expectedFromConfig = _configuration["Admin:ApiKey"];
        var expectedFromDb = await _configService.GetAdminApiKeyAsync();

        // Se não há nenhuma chave configurada, o primeiro uso é permitido
        // (bootstrap) — o caller deve então setar a chave.
        if (string.IsNullOrWhiteSpace(expectedFromConfig) &&
            string.IsNullOrWhiteSpace(expectedFromDb))
        {
            _logger.LogWarning("⚠️ Admin API Key não configurada — rota em modo bootstrap");
            return true;
        }

        if (string.IsNullOrWhiteSpace(adminKey)) return false;

        return adminKey == expectedFromConfig || adminKey == expectedFromDb;
    }

    /// <summary>
    /// 🔑 Configurar Admin API Key (persiste no banco)
    /// </summary>
    [HttpPost("admin-key")]
    public async Task<IActionResult> SetAdminKey(
        [FromBody] SetAdminKeyRequest request,
        [FromHeader(Name = "X-Admin-Key")] string? adminKey)
    {
        if (!await IsAdminAuthorizedAsync(adminKey))
            return Unauthorized(new { message = "Acesso negado" });

        if (string.IsNullOrWhiteSpace(request.Key))
            return BadRequest(new { message = "Admin key não pode ser vazia" });

        if (request.Key.Length < 8)
            return BadRequest(new { message = "Admin key deve ter pelo menos 8 caracteres" });

        await _configService.SetAdminApiKeyAsync(request.Key);
        _logger.LogInformation("🔑 Admin API Key atualizada");

        return Ok(new { message = "Admin key configurada com sucesso", updatedAt = DateTime.UtcNow });
    }

    /// <summary>
    /// 🔒 Configurar Cloudia API Key
    /// </summary>
    [HttpPost("cloudia-api-key")]
    public async Task<IActionResult> SetCloudiaApiKey(
        [FromBody] SetApiKeyRequest request,
        [FromHeader(Name = "X-Admin-Key")] string? adminKey)
    {
        if (!await IsAdminAuthorizedAsync(adminKey))
        {
            _logger.LogWarning(
                "⚠️ Tentativa de acesso não autorizado - IP: {IP}",
                HttpContext.Connection.RemoteIpAddress);
            return Unauthorized(new { message = "Acesso negado" });
        }

        if (string.IsNullOrWhiteSpace(request.ApiKey))
            return BadRequest(new { message = "API Key não pode ser vazia" });

        DateTime? expiresAt = request.ExpiresAt;
        if (!expiresAt.HasValue && request.ExpiresInDays.HasValue)
            expiresAt = DateTime.UtcNow.AddDays(request.ExpiresInDays.Value);

        await _configService.SetCloudiaApiKeyAsync(request.ApiKey, expiresAt);

        _logger.LogInformation(
            "🔑 Cloudia API Key atualizada | Expira em: {ExpiresAt}",
            expiresAt);

        return Ok(new
        {
            message = "API Key configurada com sucesso",
            expiresAt,
            configuredAt = DateTime.UtcNow
        });
    }

    /// <summary>
    /// 🔒 Verificar status da API Key
    /// </summary>
    [HttpGet("cloudia-api-key/status")]
    public async Task<IActionResult> GetCloudiaApiKeyStatus(
        [FromHeader(Name = "X-Admin-Key")] string? adminKey)
    {
        if (!await IsAdminAuthorizedAsync(adminKey))
            return Unauthorized(new { message = "Acesso negado" });

        var config = await _configService.GetConfigurationAsync("CLOUDIA_API_KEY");
        var isValid = await _configService.IsCloudiaApiKeyValidAsync();

        return Ok(new
        {
            configured = config is not null,
            isValid,
            keyPreview = config is not null && config.Value.Length >= 30
                ? $"{config.Value[..20]}...{config.Value[^10..]}"
                : null,
            createdAt = config?.CreatedAt,
            updatedAt = config?.UpdatedAt,
            expiresAt = config?.ExpiresAt,
            checkedAt = DateTime.UtcNow
        });
    }

    /// <summary>
    /// ❌ Deletar API Key
    /// </summary>
    [HttpDelete("cloudia-api-key")]
    public async Task<IActionResult> DeleteCloudiaApiKey(
        [FromHeader(Name = "X-Admin-Key")] string? adminKey)
    {
        if (!await IsAdminAuthorizedAsync(adminKey))
            return Unauthorized(new { message = "Acesso negado" });

        var removed = await _configService.DeleteConfigurationAsync("CLOUDIA_API_KEY");
        if (!removed)
            return NotFound(new { message = "API Key não encontrada" });

        _logger.LogWarning("🗑️ Cloudia API Key removida manualmente");

        return Ok(new
        {
            message = "API Key removida",
            removedAt = DateTime.UtcNow
        });
    }
}

public class SetApiKeyRequest
{
    public string ApiKey { get; set; } = null!;
    public int? ExpiresInDays { get; set; }
    public DateTime? ExpiresAt { get; set; }
}

public class SetAdminKeyRequest
{
    public string Key { get; set; } = null!;
}
