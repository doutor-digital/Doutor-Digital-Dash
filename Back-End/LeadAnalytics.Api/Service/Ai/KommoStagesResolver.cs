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

    /// <summary>
    /// As etapas da conta com o FUNIL de cada uma — a base dos outros métodos. Cacheada por
    /// 1h por unidade. Falha em silêncio (lista vazia) se a Kommo não responder.
    /// </summary>
    public async Task<IReadOnlyList<EtapaDaConta>> GetEtapasAsync(int unitId, CancellationToken ct)
    {
        var cacheKey = $"kommo-stages-list:{unitId}";
        if (cache.TryGetValue(cacheKey, out IReadOnlyList<EtapaDaConta>? cached) && cached is not null)
            return cached;

        var etapas = new List<EtapaDaConta>();
        var unit = await db.Units.AsNoTracking().FirstOrDefaultAsync(u => u.Id == unitId, ct);
        if (unit is null || string.IsNullOrWhiteSpace(unit.KommoSubdomain) || string.IsNullOrWhiteSpace(unit.KommoAccessToken))
        {
            cache.Set<IReadOnlyList<EtapaDaConta>>(cacheKey, etapas, TimeSpan.FromMinutes(5));
            return etapas;
        }

        try
        {
            var resp = await api.GetPipelinesAsync(unit.KommoSubdomain!, unit.KommoAccessToken!, ct);
            foreach (var pipeline in resp?.Embedded?.Pipelines ?? new())
                foreach (var status in pipeline.Embedded?.Statuses ?? new())
                    if (!string.IsNullOrWhiteSpace(status.Name))
                        etapas.Add(new EtapaDaConta(pipeline.Id, status.Id, status.Name));
            logger.LogInformation("[kommo-stages] unit={Unit} carregou {Count} etapas", unitId, etapas.Count);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[kommo-stages] falha ao buscar pipelines (unit={Unit})", unitId);
        }

        cache.Set<IReadOnlyList<EtapaDaConta>>(cacheKey, etapas, Ttl);
        return etapas;
    }

    /// <summary>
    /// <c>status_id → nome</c>, sem funil. Ambíguo para 142/143 (o último funil lido vence) —
    /// serve para mostrar nome em texto livre, NUNCA para decidir a etapa canônica. Para
    /// isso use <see cref="GetCanonicalStageMapAsync"/> ou <see cref="GetEtapasAsync"/>.
    /// </summary>
    public async Task<Dictionary<int, string>> GetStageMapAsync(int unitId, CancellationToken ct)
    {
        var map = new Dictionary<int, string>();
        foreach (var e in await GetEtapasAsync(unitId, ct))
            map[(int)e.Etapa] = e.Nome!;
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
    /// Mapa <c>etapa da Kommo → etapa canônica</c> derivado dos NOMES das etapas, com a
    /// chave <c>"{funil}:{etapa}"</c> (e a chave só com o número quando ele não é ambíguo).
    /// Usado pelo webhook ao vivo como <c>stageMapOverride</c> do
    /// <see cref="KommoIngestionService"/> — o MESMO construtor que o sync usa
    /// (<see cref="MapaCanonicoDeEtapas"/>).
    ///
    /// Antes este mapa era só por número: o 143 do funil TRATAMENTO ("TRATAMENTO
    /// CANCELADO") sobrescrevia o 143 do COMERCIAL ("PERDIDO"), e todo lead perdido que
    /// chegava pelo webhook era gravado como tratamento cancelado (Açailândia, 16/10/2026:
    /// 180 de 180).
    /// </summary>
    public async Task<Dictionary<string, string>> GetCanonicalStageMapAsync(int unitId, CancellationToken ct)
        => MapaCanonicoDeEtapas.Montar(await GetEtapasAsync(unitId, ct));
}
