using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace LeadAnalytics.Api.Service.Ads;

/// <summary>Credenciais resolvidas de um provedor (do banco ou do appsettings).</summary>
public record AdsCredentials(string Provider, string? ClientId, string? ClientSecret, string? DeveloperToken)
{
    /// <summary>Tem o necessário pra operar em modo real?</summary>
    public bool IsConfigured => Provider == "google"
        ? !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret) && !string.IsNullOrWhiteSpace(DeveloperToken)
        : !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);
}

/// <summary>De onde a credencial veio (pra UI informar).</summary>
public enum AdsCredentialsSource { None, Config, Db }

/// <summary>Estado das credenciais p/ leitura (NUNCA expõe o segredo em texto).</summary>
public record AdsCredentialsStatus(
    string Provider,
    string? ClientId,
    bool HasSecret,
    string? DeveloperToken,
    bool Live,
    AdsCredentialsSource Source);

/// <summary>
/// Lê/grava as credenciais do app de anúncios. Prioridade: linha em <c>ads_settings</c> →
/// variáveis de ambiente (<c>Ads:Meta:*</c>/<c>Ads:Google:*</c>). O segredo é cifrado no banco
/// via <see cref="ProtectedTokenService"/>.
/// </summary>
public class AdsCredentialsService(AppDbContext db, ProtectedTokenService tokens, IConfiguration config)
{
    public async Task<AdsCredentials> GetAsync(string provider, CancellationToken ct = default)
    {
        var row = await db.AdsSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Provider == provider, ct);
        if (row is not null && !string.IsNullOrWhiteSpace(row.ClientId))
            return new AdsCredentials(provider, row.ClientId, tokens.Unprotect(row.ClientSecretEnc), row.DeveloperToken);

        // Fallback: variáveis de ambiente / appsettings.
        return provider == "google"
            ? new AdsCredentials(provider, config["Ads:Google:ClientId"], config["Ads:Google:ClientSecret"], config["Ads:Google:DeveloperToken"])
            : new AdsCredentials(provider, config["Ads:Meta:AppId"], config["Ads:Meta:AppSecret"], null);
    }

    public async Task<AdsCredentialsStatus> GetStatusAsync(string provider, CancellationToken ct = default)
    {
        var row = await db.AdsSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Provider == provider, ct);
        var fromDb = row is not null && !string.IsNullOrWhiteSpace(row.ClientId);
        var creds = await GetAsync(provider, ct);
        var source = fromDb ? AdsCredentialsSource.Db
            : creds.IsConfigured ? AdsCredentialsSource.Config
            : AdsCredentialsSource.None;
        return new AdsCredentialsStatus(
            provider,
            creds.ClientId,
            !string.IsNullOrWhiteSpace(creds.ClientSecret),
            creds.DeveloperToken,
            creds.IsConfigured,
            source);
    }

    /// <summary>Upsert das credenciais. O segredo só é trocado se vier preenchido (mantém o anterior).</summary>
    public async Task SaveAsync(
        string provider, string? clientId, string? clientSecret, string? developerToken,
        string? email, CancellationToken ct = default)
    {
        var row = await db.AdsSettings.FirstOrDefaultAsync(s => s.Provider == provider, ct);
        if (row is null)
        {
            row = new AdsSetting { Provider = provider };
            db.AdsSettings.Add(row);
        }
        row.ClientId = string.IsNullOrWhiteSpace(clientId) ? row.ClientId : clientId.Trim();
        if (!string.IsNullOrWhiteSpace(clientSecret))
            row.ClientSecretEnc = tokens.Protect(clientSecret.Trim());
        row.DeveloperToken = string.IsNullOrWhiteSpace(developerToken) ? row.DeveloperToken : developerToken.Trim();
        row.UpdatedByEmail = email;
        row.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }
}
