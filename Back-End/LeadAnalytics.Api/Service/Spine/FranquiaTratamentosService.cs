using LeadAnalytics.Api.DTOs.Spine;
using Microsoft.Extensions.Caching.Memory;

namespace LeadAnalytics.Api.Service.Spine;

/// <summary>
/// Situação dos tratamentos da unidade (raspado do CRM web da franquia), com cache.
/// Login+export é caro (~2-4s), então cacheia por unidade+janela. A situação muda
/// devagar — cache de minutos é suficiente e evita raspar a cada request.
/// </summary>
public class FranquiaTratamentosService(
    FranquiaWebStore store,
    FranquiaWebClient client,
    IMemoryCache cache,
    ILogger<FranquiaTratamentosService> logger)
{
    private readonly FranquiaWebStore _store = store;
    private readonly FranquiaWebClient _client = client;
    private readonly IMemoryCache _cache = cache;
    private readonly ILogger<FranquiaTratamentosService> _logger = logger;

    /// <summary>
    /// Devolve a agregação por situação/valor. Retorna null se a unidade não tiver
    /// credenciais/idCompany configurados (o controller vira 503 com instrução).
    /// </summary>
    public async Task<FranquiaTratamentosDto?> GetAsync(
        int unitId, DateOnly de, DateOnly ate, CancellationToken ct = default)
    {
        var creds = await _store.GetAsync(unitId, ct);
        if (creds is null) return null;

        var key = $"franquia:trat:{unitId}:{de:yyyyMMdd}:{ate:yyyyMMdd}";
        if (_cache.TryGetValue(key, out FranquiaTratamentosDto? cached) && cached is not null)
            return cached;

        var (email, pass, idCompany) = creds.Value;
        var dto = await _client.GetTratamentosAsync(email, pass, idCompany, de, ate, ct);
        _cache.Set(key, dto, TimeSpan.FromSeconds(300));
        _logger.LogInformation("Franquia web: {Total} tratamentos (unit {Unit}, {De}..{Ate})",
            dto.Total, unitId, de, ate);
        return dto;
    }
}
