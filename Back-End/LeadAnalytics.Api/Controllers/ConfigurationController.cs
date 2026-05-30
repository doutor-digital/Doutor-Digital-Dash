using LeadAnalytics.Api.Service;
using Microsoft.AspNetCore.Mvc;

namespace LeadAnalytics.Api.Controllers;

[ApiController]
[Route("api/config")]
public class ConfigurationController(
    ConfigurationService configService,
    ILogger<ConfigurationController> logger,
    IConfiguration configuration,
    ICurrentUser currentUser) : ControllerBase
{
    private readonly ConfigurationService _configService = configService;
    private readonly ILogger<ConfigurationController> _logger = logger;
    private readonly IConfiguration _configuration = configuration;
    private readonly ICurrentUser _currentUser = currentUser;

    private async Task<bool> IsAdminAuthorizedAsync(string? adminKey)
    {
        // Super-admin autenticado via JWT acessa direto, sem precisar
        // do header X-Admin-Key separado.
        if (_currentUser.IsSuperAdmin)
            return true;

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

}

public class SetAdminKeyRequest
{
    public string Key { get; set; } = null!;
}
