using LeadAnalytics.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace LeadAnalytics.Api.Service.Ai;

/// <summary>
/// Resolve <c>status_id</c> da Kommo (ex.: 106037707) para o nome humano
/// (ex.: "Lead de entrada"). Cacheia por 1h por unidade — pipelines mudam
/// raramente. Falha em silêncio (devolve dict vazio) se Kommo não responder.
/// </summary>
public class KommoStagesResolver(
    AppDbContext db,
    KommoApiClient api,
    IMemoryCache cache,
    ILogger<KommoStagesResolver> logger)
{
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(1);

    public async Task<Dictionary<int, string>> GetStageMapAsync(int unitId, CancellationToken ct)
    {
        var cacheKey = $"kommo-stages-map:{unitId}";
        if (cache.TryGetValue(cacheKey, out Dictionary<int, string>? cached) && cached is not null)
            return cached;

        var map = new Dictionary<int, string>();
        var unit = await db.Units.AsNoTracking().FirstOrDefaultAsync(u => u.Id == unitId, ct);
        if (unit is null || string.IsNullOrWhiteSpace(unit.KommoSubdomain) || string.IsNullOrWhiteSpace(unit.KommoAccessToken))
        {
            cache.Set(cacheKey, map, TimeSpan.FromMinutes(5));
            return map;
        }

        try
        {
            var resp = await api.GetPipelinesAsync(unit.KommoSubdomain!, unit.KommoAccessToken!, ct);
            foreach (var pipeline in resp?.Embedded?.Pipelines ?? new())
            {
                foreach (var status in pipeline.Embedded?.Statuses ?? new())
                {
                    if (!string.IsNullOrWhiteSpace(status.Name))
                        map[(int)status.Id] = status.Name!;
                }
            }
            logger.LogInformation("[kommo-stages] unit={Unit} carregou {Count} etapas", unitId, map.Count);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[kommo-stages] falha ao buscar pipelines (unit={Unit})", unitId);
        }

        cache.Set(cacheKey, map, Ttl);
        return map;
    }

    /// <summary>Atalho: resolve um stage_id direto (ou devolve null).</summary>
    public async Task<string?> ResolveAsync(int unitId, int? stageId, CancellationToken ct)
    {
        if (stageId is not int id) return null;
        var map = await GetStageMapAsync(unitId, ct);
        return map.TryGetValue(id, out var name) ? name : null;
    }
}
