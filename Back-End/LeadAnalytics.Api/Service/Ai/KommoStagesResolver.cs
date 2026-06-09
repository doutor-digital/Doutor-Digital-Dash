using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.Service.Stages;
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

    /// <summary>
    /// Mapa <c>status_id (string) → etapa canônica</c> derivado dos NOMES das etapas
    /// na Kommo (reusa o cache de <see cref="GetStageMapAsync"/>). Usado pelo webhook
    /// ao vivo como <c>stageMapOverride</c> do <see cref="KommoIngestionService"/>,
    /// pra resolver agendados mesmo quando a unidade NÃO tem <c>KommoStageMapJson</c>
    /// preenchido — análogo ao que o sync já faz.
    /// </summary>
    public async Task<Dictionary<string, string>> GetCanonicalStageMapAsync(int unitId, CancellationToken ct)
    {
        var byId = await GetStageMapAsync(unitId, ct);
        var canonical = new Dictionary<string, string>(byId.Count);
        foreach (var (id, name) in byId)
        {
            var c = CanonicalStages.Resolve(name);
            if (c != null) canonical[id.ToString()] = c;
        }
        return canonical;
    }
}
