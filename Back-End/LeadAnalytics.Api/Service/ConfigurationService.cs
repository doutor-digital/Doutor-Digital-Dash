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

    public async Task<AppConfiguration?> GetConfigurationAsync(string key)
    {
        return await _db.AppConfigurations
            .FirstOrDefaultAsync(c => c.Key == key);
    }

    public async Task<string?> GetAdminApiKeyAsync()
    {
        var config = await _db.AppConfigurations
            .FirstOrDefaultAsync(c => c.Key == "ADMIN_API_KEY");
        return config?.Value;
    }

    public async Task SetAdminApiKeyAsync(string key)
    {
        var config = await _db.AppConfigurations
            .FirstOrDefaultAsync(c => c.Key == "ADMIN_API_KEY");

        if (config is null)
        {
            config = new AppConfiguration
            {
                Key = "ADMIN_API_KEY",
                Value = key,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            _db.AppConfigurations.Add(config);
        }
        else
        {
            config.Value = key;
            config.UpdatedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync();
    }

    public async Task<bool> DeleteConfigurationAsync(string key)
    {
        var config = await _db.AppConfigurations
            .FirstOrDefaultAsync(c => c.Key == key);
        if (config is null) return false;
        _db.AppConfigurations.Remove(config);
        await _db.SaveChangesAsync();
        return true;
    }
}