using LeadAnalytics.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace LeadAnalytics.Api.Service.Spine;

/// <summary>
/// Resolve o token da API Spine por unidade.
///
/// O token é emitido pelo suporte do Doutor Hérnia com escopo de unidade (idCompany)
/// e permissões por módulo. Nunca fica no repositório: é lido de AppConfiguration
/// (chave "spine:token:{unitId}") e, se não houver, da env var SPINE_TOKEN — que
/// cobre o caso de unidade única enquanto a Central de Integrações não expõe a tela.
/// </summary>
public class SpineTokenStore(AppDbContext db, ILogger<SpineTokenStore> logger)
{
    private readonly AppDbContext _db = db;
    private readonly ILogger<SpineTokenStore> _logger = logger;

    public static string KeyFor(int unitId) => $"spine:token:{unitId}";

    public async Task<string?> GetTokenAsync(int unitId, CancellationToken ct = default)
    {
        var cfg = await _db.AppConfigurations
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Key == KeyFor(unitId), ct);

        if (!string.IsNullOrWhiteSpace(cfg?.Value))
            return cfg!.Value.Trim();

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
}
