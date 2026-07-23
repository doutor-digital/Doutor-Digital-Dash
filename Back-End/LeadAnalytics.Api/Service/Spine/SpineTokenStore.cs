using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace LeadAnalytics.Api.Service.Spine;

/// <summary>
/// Guarda e resolve o token da API Spine por unidade — a base do onboarding
/// self-service: cada franquia cola o próprio token do Doutor Hérnia na Central
/// de Integrações e o dado passa a aparecer, sem ninguém mexer no banco.
///
/// O token é gravado CIFRADO (DataProtection, mesmo esquema dos tokens de Ads).
/// Para não quebrar tokens inseridos à mão antes disso, a leitura tenta decifrar
/// e, se falhar, assume texto puro (legado) — que continua funcionando.
/// Fallback final: env var SPINE_TOKEN (unidade única / desenvolvimento).
/// </summary>
public class SpineTokenStore(
    AppDbContext db,
    ProtectedTokenService protector,
    ILogger<SpineTokenStore> logger)
{
    private readonly AppDbContext _db = db;
    private readonly ProtectedTokenService _protector = protector;
    private readonly ILogger<SpineTokenStore> _logger = logger;

    public static string KeyFor(int unitId) => $"spine:token:{unitId}";

    public async Task<string?> GetTokenAsync(int unitId, CancellationToken ct = default)
    {
        var cfg = await _db.AppConfigurations
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Key == KeyFor(unitId), ct);

        if (!string.IsNullOrWhiteSpace(cfg?.Value))
        {
            // Grava cifrado; tokens antigos ficaram em texto puro. Unprotect devolve
            // null quando o valor não foi cifrado por nós → cai no valor cru.
            var claro = _protector.Unprotect(cfg!.Value);
            return (claro ?? cfg.Value).Trim();
        }

        var env = Environment.GetEnvironmentVariable("SPINE_TOKEN");
        if (!string.IsNullOrWhiteSpace(env))
        {
            _logger.LogDebug("Spine: usando SPINE_TOKEN da env para a unidade {UnitId}", unitId);
            return env.Trim();
        }

        _logger.LogWarning("Spine: nenhum token configurado para a unidade {UnitId} " +
                           "(esperado AppConfiguration '{Key}' ou env SPINE_TOKEN)", unitId, KeyFor(unitId));
        return null;
    }

    /// <summary>Grava (ou substitui) o token da unidade, sempre cifrado.</summary>
    public async Task SaveTokenAsync(int unitId, string token, CancellationToken ct = default)
    {
        var key = KeyFor(unitId);
        var cifrado = _protector.Protect(token.Trim())!;

        var cfg = await _db.AppConfigurations.FirstOrDefaultAsync(c => c.Key == key, ct);
        if (cfg is null)
        {
            _db.AppConfigurations.Add(new AppConfiguration
            {
                Key = key,
                Value = cifrado,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            });
        }
        else
        {
            cfg.Value = cifrado;
            cfg.UpdatedAt = DateTime.UtcNow;
        }
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Spine: token gravado para a unidade {UnitId}", unitId);
    }

    /// <summary>Status para a tela: configurado? quando? prévia mascarada.</summary>
    public async Task<(bool Configurado, DateTime? Atualizado, string? Previa)> GetStatusAsync(
        int unitId, CancellationToken ct = default)
    {
        var cfg = await _db.AppConfigurations
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Key == KeyFor(unitId), ct);

        if (cfg is null || string.IsNullOrWhiteSpace(cfg.Value))
            return (false, null, null);

        var claro = (_protector.Unprotect(cfg.Value) ?? cfg.Value).Trim();
        // Um JWT nunca deve ser exibido inteiro. Mostra só as pontas.
        var previa = claro.Length <= 12 ? "••••" : $"{claro[..6]}…{claro[^4..]}";
        return (true, cfg.UpdatedAt, previa);
    }

    /// <summary>Remove o token da unidade.</summary>
    public async Task<bool> DeleteTokenAsync(int unitId, CancellationToken ct = default)
    {
        var cfg = await _db.AppConfigurations.FirstOrDefaultAsync(c => c.Key == KeyFor(unitId), ct);
        if (cfg is null) return false;
        _db.AppConfigurations.Remove(cfg);
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Spine: token removido da unidade {UnitId}", unitId);
        return true;
    }
}
