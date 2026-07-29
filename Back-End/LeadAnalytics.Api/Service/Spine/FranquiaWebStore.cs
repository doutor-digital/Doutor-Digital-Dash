using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace LeadAnalytics.Api.Service.Spine;

/// <summary>
/// Credenciais do CRM WEB da franquia (login por sessão) + o idCompany da unidade
/// no CRM deles. Email/senha são compartilhados (uma conta) e ficam CIFRADOS em
/// AppConfiguration; o idCompany é por unidade (ex.: unit 15 = Imperatriz = 133).
/// Segue o mesmo esquema do <see cref="SpineTokenStore"/> (Unprotect com fallback texto puro).
/// </summary>
public class FranquiaWebStore(
    AppDbContext db,
    ProtectedTokenService protector,
    ILogger<FranquiaWebStore> logger)
{
    private readonly AppDbContext _db = db;
    private readonly ProtectedTokenService _protector = protector;
    private readonly ILogger<FranquiaWebStore> _logger = logger;

    public const string EmailKey = "franquia:web:email";
    public const string PasswordKey = "franquia:web:password";
    public static string CompanyKeyFor(int unitId) => $"franquia:web:idcompany:{unitId}";

    private async Task<string?> ReadAsync(string key, CancellationToken ct)
    {
        var cfg = await _db.AppConfigurations.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Key == key, ct);
        if (string.IsNullOrWhiteSpace(cfg?.Value)) return null;
        return (_protector.Unprotect(cfg.Value) ?? cfg.Value).Trim();
    }

    /// <summary>(email, senha, idCompany) da unidade, ou null se não configurado.</summary>
    public async Task<(string Email, string Password, int IdCompany)?> GetAsync(int unitId, CancellationToken ct = default)
    {
        var email = await ReadAsync(EmailKey, ct);
        var pass = await ReadAsync(PasswordKey, ct);
        var comp = await ReadAsync(CompanyKeyFor(unitId), ct);
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(pass) || !int.TryParse(comp, out var idCompany))
        {
            _logger.LogWarning("Franquia web: credenciais/idCompany não configurados para a unidade {UnitId}", unitId);
            return null;
        }
        return (email!, pass!, idCompany);
    }

    public async Task<bool> IsConfiguredAsync(int unitId, CancellationToken ct = default)
        => (await GetAsync(unitId, ct)) is not null;
}
