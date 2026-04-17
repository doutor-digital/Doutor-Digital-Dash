using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace LeadAnalytics.Api.Service;

public class ConfigurationService(
    AppDbContext db,
    ILogger<ConfigurationService> logger,
    IConfiguration configuration)
{
    private readonly AppDbContext _db = db;
    private readonly ILogger<ConfigurationService> _logger = logger;
    private readonly IConfiguration _configuration = configuration;

    public async Task<string?> GetCloudiaApiKeyAsync()
    {
        var envKey = _configuration["Cloudia:ApiKey"];
        if (!string.IsNullOrWhiteSpace(envKey) && envKey != "DEIXE_VAZIO_AQUI")
        {
            _logger.LogInformation("🔑 Usando Cloudia API Key de variável de ambiente");
            return envKey;
        }

        var config = await _db.AppConfigurations
            .FirstOrDefaultAsync(c => c.Key == "CLOUDIA_API_KEY");

        if (config is null)
        {
            _logger.LogWarning("⚠️ Cloudia API Key não configurada!");
            return null;
        }

        if (config.ExpiresAt.HasValue && config.ExpiresAt.Value < DateTime.UtcNow)
        {
            _logger.LogWarning(
                "⚠️ Cloudia API Key expirada! (Expirou em {ExpiresAt})",
                config.ExpiresAt.Value);
            return null;
        }

        if(_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation(
                "🔑 Usando Cloudia API Key do banco (atualizada em {UpdatedAt})",
                config.UpdatedAt);
        }
        
        return config.Value;
    }

    public async Task SetCloudiaApiKeyAsync(string apiKey, DateTime? expiresAt = null)
    {
        var config = await _db.AppConfigurations
            .FirstOrDefaultAsync(c => c.Key == "CLOUDIA_API_KEY");

        if (config is null)
        {
            config = new AppConfiguration
            {
                Key = "CLOUDIA_API_KEY",
                Value = apiKey,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                ExpiresAt = expiresAt
            };
            _db.AppConfigurations.Add(config);
            _logger.LogInformation("✅ Cloudia API Key criada no banco");
        }
        else
        {
            config.Value = apiKey;
            config.UpdatedAt = DateTime.UtcNow;
            config.ExpiresAt = expiresAt;
            _logger.LogInformation("🔄 Cloudia API Key atualizada no banco");
        }

        await _db.SaveChangesAsync();
    }

    public async Task<bool> IsCloudiaApiKeyValidAsync()
    {
        var key = await GetCloudiaApiKeyAsync();
        return !string.IsNullOrWhiteSpace(key);
    }

    public async Task<AppConfiguration?> GetConfigurationAsync(string key)
    {
        return await _db.AppConfigurations
            .FirstOrDefaultAsync(c => c.Key == key);
    }
}