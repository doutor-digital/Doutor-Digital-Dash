using LeadAnalytics.Api.Data;
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

    /// <summary>
    /// 🔒 Configurar Cloudia API Key (recebe do n8n)
    /// </summary>
    [HttpPost("cloudia-api-key")]
    public async Task<IActionResult> SetCloudiaApiKey(
        [FromBody] SetApiKeyRequest request,
        [FromHeader(Name = "X-Admin-Key")] string? adminKey)
    {
        var expectedAdminKey = _configuration["Admin:ApiKey"];

        if (string.IsNullOrWhiteSpace(expectedAdminKey))
        {
            _logger.LogWarning("⚠️ Admin:ApiKey não configurada nas variáveis de ambiente!");
            return StatusCode(500, new 
            { 
                message = "Admin API Key não configurada no servidor",
                hint = "Configure ADMIN__APIKEY no Railway"
            });
        }

        if (string.IsNullOrWhiteSpace(adminKey) || adminKey != expectedAdminKey)
        {
            _logger.LogWarning(
                "⚠️ Tentativa de acesso não autorizado - IP: {IP}",
                HttpContext.Connection.RemoteIpAddress);
            
            return Unauthorized(new { message = "Acesso negado" });
        }

        if (string.IsNullOrWhiteSpace(request.ApiKey))
        {
            return BadRequest(new { message = "API Key não pode ser vazia" });
        }

        if (request.ApiKey.Length < 50)
        {
            return BadRequest(new { message = "API Key parece inválida (muito curta)" });
        }

        // ═══════════════════════════════════════════════════════
        // 💾 SALVAR TOKEN
        // ═══════════════════════════════════════════════════════
        var expiresAt = request.ExpiresInDays.HasValue
            ? DateTime.UtcNow.AddDays(request.ExpiresInDays.Value)
            : DateTime.UtcNow.AddDays(7); // Padrão: 7 dias

        await _configService.SetCloudiaApiKeyAsync(request.ApiKey, expiresAt);

        _logger.LogInformation(
            "🔑 Cloudia API Key atualizada | Expira em: {ExpiresAt}",
            expiresAt);

        return Ok(new
        {
            message = "API Key configurada com sucesso",
            expires_at = expiresAt,
            configured_at = DateTime.UtcNow,
            token_preview = $"{request.ApiKey[..20]}...{request.ApiKey[^10..]}"
        });
    }

    /// <summary>
    /// 🔒 Verificar status da API Key
    /// </summary>
    [HttpGet("cloudia-api-key/status")]
    public async Task<IActionResult> GetCloudiaApiKeyStatus(
        [FromHeader(Name = "X-Admin-Key")] string? adminKey)
    {
        var expectedAdminKey = _configuration["Admin:ApiKey"];

        if (string.IsNullOrWhiteSpace(adminKey) || adminKey != expectedAdminKey)
        {
            return Unauthorized(new { message = "Acesso negado" });
        }

        var config = await _configService.GetConfigurationAsync("CLOUDIA_API_KEY");
        var isValid = await _configService.IsCloudiaApiKeyValidAsync();

        return Ok(new
        {
            is_configured = config is not null,
            is_valid = isValid,
            key_preview = config is not null
                ? $"{config.Value[..20]}...{config.Value[^10..]}"
                : null,
            created_at = config?.CreatedAt,
            updated_at = config?.UpdatedAt,
            expires_at = config?.ExpiresAt,
            checked_at = DateTime.UtcNow
        });
    }

    /// <summary>
    /// ❌ Deletar API Key (forçar renovação)
    /// </summary>
    [HttpDelete("cloudia-api-key")]
    public async Task<IActionResult> DeleteCloudiaApiKey(
        [FromHeader(Name = "X-Admin-Key")] string? adminKey)
    {
        var expectedAdminKey = _configuration["Admin:ApiKey"];

        if (string.IsNullOrWhiteSpace(adminKey) || adminKey != expectedAdminKey)
        {
            return Unauthorized(new { message = "Acesso negado" });
        }

        var config = await _configService.GetConfigurationAsync("CLOUDIA_API_KEY");
        
        if (config is null)
        {
            return NotFound(new { message = "API Key não encontrada" });
        }

        var db = _configService.GetType()
            .GetField("_db", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(_configService) as AppDbContext;

        db!.AppConfigurations.Remove(config);
        await db.SaveChangesAsync();

        _logger.LogWarning("🗑️ Cloudia API Key removida manualmente");

        return Ok(new 
        { 
            message = "API Key removida - será renovada no próximo cron",
            removed_at = DateTime.UtcNow
        });
    }
}

public class SetApiKeyRequest
{
    public string ApiKey { get; set; } = null!;
    public int? ExpiresInDays { get; set; } = 7;
}