using LeadAnalytics.Api.DTOs.Spine;
using Microsoft.Extensions.Caching.Memory;

namespace LeadAnalytics.Api.Service.Spine;

/// <summary>
/// Situação dos tratamentos da unidade, com cache.
///
/// Fonte: a rota oficial <c>/api/treatments/search</c>, com o export raspado do CRM web
/// como reserva.
///
/// A rota devolvia pouquíssimos tratamentos (2 em Imperatriz) e isso ERA defeito nosso,
/// ao contrário do que este comentário afirmava antes: mandávamos
/// <c>initialDate</c>/<c>endDate</c>, que esta rota não conhece, e a API caía no padrão
/// dela — o mês corrente. O card ficava preso no mesmo número em qualquer período.
/// Corrigido para <c>initialCreatedDate</c>/<c>endCreatedDate</c> em SpineApiClient.
///
/// A reserva existe para unidade sem token da API — nela o export continua sendo a
/// única fonte.
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
    /// <param name="fonte">
    /// <c>api</c> ou <c>web</c> força uma das fontes; vazio usa a ordem padrão. Existe
    /// para conferir uma contra a outra sem trocar código — as duas divergem, e sem poder
    /// comparar lado a lado a investigação vira tentativa e erro em produção.
    /// </param>
    public async Task<FranquiaTratamentosDto?> GetAsync(
        int unitId, DateOnly de, DateOnly ate, CancellationToken ct = default, string? fonte = null)
    {
        var key = $"franquia:trat:{unitId}:{de:yyyyMMdd}:{ate:yyyyMMdd}:{fonte ?? "auto"}";
        if (_cache.TryGetValue(key, out FranquiaTratamentosDto? cached) && cached is not null)
            return cached;

        var dto = fonte switch
        {
            "api" => await PelaApiAsync(unitId, de, ate, ct),
            "web" => await PelaRaspagemAsync(unitId, de, ate, ct),
            _ => await PelaApiAsync(unitId, de, ate, ct) ?? await PelaRaspagemAsync(unitId, de, ate, ct),
        };
        if (dto is null) return null;

        _cache.Set(key, dto, TimeSpan.FromSeconds(300));
        _logger.LogInformation("Tratamentos: {Total} (unit {Unit}, {De}..{Ate}, fonte {Fonte})",
            dto.Total, unitId, de, ate, dto.Fonte);
        return dto;
    }

    /// <summary>Fonte principal. Null = sem token da unidade ou a API recusou.</summary>
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

    /// <summary>Reserva: export do CRM web, para unidade sem token da API.</summary>
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
