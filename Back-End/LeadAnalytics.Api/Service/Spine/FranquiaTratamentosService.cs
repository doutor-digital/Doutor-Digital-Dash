using LeadAnalytics.Api.DTOs.Spine;
using Microsoft.Extensions.Caching.Memory;

namespace LeadAnalytics.Api.Service.Spine;

/// <summary>
/// Situação dos tratamentos da unidade, com cache.
///
/// Fonte preferida: a rota oficial <c>/api/treatments/search</c>. A raspagem do CRM web
/// ficou como reserva — ela depende de login em tela, quebra quando o layout muda e leva
/// 2-4s por chamada. Só entra quando a unidade não tem token da API ou quando a API
/// recusa/erra.
///
/// O cache continua por unidade+janela: a situação de tratamento muda devagar, e
/// nenhuma das duas fontes é barata o bastante para ser consultada a cada request.
/// </summary>
public class FranquiaTratamentosService(
    FranquiaWebStore store,
    FranquiaWebClient client,
    SpineApiClient api,
    SpineTokenStore tokens,
    IMemoryCache cache,
    ILogger<FranquiaTratamentosService> logger)
{
    private readonly FranquiaWebStore _store = store;
    private readonly FranquiaWebClient _client = client;
    private readonly SpineApiClient _api = api;
    private readonly SpineTokenStore _tokens = tokens;
    private readonly IMemoryCache _cache = cache;
    private readonly ILogger<FranquiaTratamentosService> _logger = logger;

    /// <summary>
    /// Devolve a agregação por situação/valor. Retorna null quando a unidade não tem
    /// nenhuma das duas fontes configurada (o controller vira 503 com instrução).
    /// </summary>
    public async Task<FranquiaTratamentosDto?> GetAsync(
        int unitId, DateOnly de, DateOnly ate, CancellationToken ct = default)
    {
        var key = $"franquia:trat:{unitId}:{de:yyyyMMdd}:{ate:yyyyMMdd}";
        if (_cache.TryGetValue(key, out FranquiaTratamentosDto? cached) && cached is not null)
            return cached;

        var dto = await PelaApiAsync(unitId, de, ate, ct) ?? await PelaRaspagemAsync(unitId, de, ate, ct);
        if (dto is null) return null;

        _cache.Set(key, dto, TimeSpan.FromSeconds(300));
        _logger.LogInformation("Tratamentos: {Total} (unit {Unit}, {De}..{Ate}, fonte {Fonte})",
            dto.Total, unitId, de, ate, dto.Fonte);
        return dto;
    }

    /// <summary>Rota oficial. Null = sem token ou a API recusou (aí a reserva assume).</summary>
    private async Task<FranquiaTratamentosDto?> PelaApiAsync(
        int unitId, DateOnly de, DateOnly ate, CancellationToken ct)
    {
        var token = await _tokens.GetTokenAsync(unitId, ct);
        if (string.IsNullOrWhiteSpace(token)) return null;

        try
        {
            var linhas = await _api.SearchTreatmentsAsync(token, de, ate, ct);

            var porSituacao = linhas
                .GroupBy(t => string.IsNullOrWhiteSpace(t.StatusName) ? "SEM SITUAÇÃO" : t.StatusName!.Trim())
                .Select(g => new FranquiaTratamentoSituacao
                {
                    Situacao = g.Key,
                    Quantidade = g.Count(),
                    Valor = g.Sum(t => t.Price ?? 0m),
                })
                .OrderByDescending(s => s.Quantidade)
                .ToList();

            return new FranquiaTratamentosDto
            {
                Total = linhas.Count,
                ValorTotal = linhas.Sum(t => t.Price ?? 0m),
                PorSituacao = porSituacao,
                // A rota oficial não devolve pago/pendente; fica vazio de propósito.
                PorFinanceiro = [],
                Fonte = "api",
                AtualizadoEm = DateTime.UtcNow,
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Tratamentos pela API falharam (unit {Unit}); tentando o export do CRM web", unitId);
            return null;
        }
    }

    /// <summary>Reserva: export raspado do CRM web.</summary>
    private async Task<FranquiaTratamentosDto?> PelaRaspagemAsync(
        int unitId, DateOnly de, DateOnly ate, CancellationToken ct)
    {
        var creds = await _store.GetAsync(unitId, ct);
        if (creds is null) return null;

        var (email, pass, idCompany) = creds.Value;
        var dto = await _client.GetTratamentosAsync(email, pass, idCompany, de, ate, ct);
        dto.Fonte = "web";
        return dto;
    }
}
