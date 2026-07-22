namespace LeadAnalytics.Api.Service;

/// <summary>
/// Valida o header X-Admin-Key das rotas internas chamadas pelo n8n, contra a
/// mesma chave do painel (config <c>Admin:ApiKey</c> ou a persistida no banco).
/// Sem chave configurada = bootstrap (primeiro uso liberado), igual ao painel.
/// </summary>
public class InternalApiKeyGuard(ConfigurationService configService, IConfiguration configuration)
{
    private readonly ConfigurationService _configService = configService;
    private readonly IConfiguration _configuration = configuration;

    public async Task<bool> IsAuthorizedAsync(string? key)
    {
        var expectedFromConfig = _configuration["Admin:ApiKey"];
        var expectedFromDb = await _configService.GetAdminApiKeyAsync();

        if (string.IsNullOrWhiteSpace(expectedFromConfig) &&
            string.IsNullOrWhiteSpace(expectedFromDb))
            return true;

        if (string.IsNullOrWhiteSpace(key)) return false;
        return key == expectedFromConfig || key == expectedFromDb;
    }
}
