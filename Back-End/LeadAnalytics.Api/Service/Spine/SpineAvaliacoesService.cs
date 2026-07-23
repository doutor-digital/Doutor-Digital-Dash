using LeadAnalytics.Api.DTOs.Spine;
using LeadAnalytics.Api.Options;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace LeadAnalytics.Api.Service.Spine;

/// <summary>
/// Monta o card de Avaliações a partir da agenda do Spine.
///
/// Por que só Avaliação (idCategory=1) e não a agenda inteira: avaliação é a
/// consulta que fecha o funil comercial — é o "compareceu" que hoje o dashboard
/// lê de um campo digitado na Kommo. Sessão (idCategory=2) é tratamento em curso
/// e merece card próprio.
/// </summary>
public class SpineAvaliacoesService(
    SpineApiClient client,
    SpineTokenStore tokens,
    IMemoryCache cache,
    IOptions<SpineOptions> options,
    ILogger<SpineAvaliacoesService> logger)
{
    private readonly SpineApiClient _client = client;
    private readonly SpineTokenStore _tokens = tokens;
    private readonly IMemoryCache _cache = cache;
    private readonly SpineOptions _options = options.Value;
    private readonly ILogger<SpineAvaliacoesService> _logger = logger;

    /// <summary>
    /// Os seis status da agenda, na ordem em que a operação lê o desfecho:
    /// o que aconteceu, o que falhou, o que foi devolvido, o que ainda vem.
    /// </summary>
    private static readonly (int Id, string Nome, string Grupo)[] Situacoes =
    [
        (SpineApiClient.ScheduleStatus.Atendido,      "Atendido",       "realizado"),
        (SpineApiClient.ScheduleStatus.NaoCompareceu, "Não compareceu", "falta"),
        (SpineApiClient.ScheduleStatus.Desmarcado,    "Desmarcado",     "cancelado"),
        (SpineApiClient.ScheduleStatus.Remarcado,     "Remarcado",      "cancelado"),
        (SpineApiClient.ScheduleStatus.Confirmado,    "Confirmado",     "pendente"),
        (SpineApiClient.ScheduleStatus.Agendado,      "Agendado",       "pendente"),
    ];

    public async Task<SpineAvaliacoesDto?> GetAsync(
        int unitId, DateOnly de, DateOnly ate, CancellationToken ct = default)
    {
        var cacheKey = $"spine:avaliacoes:{unitId}:{de:yyyyMMdd}:{ate:yyyyMMdd}";
        if (_cache.TryGetValue<SpineAvaliacoesDto>(cacheKey, out var hit) && hit is not null)
            return hit;

        var token = await _tokens.GetTokenAsync(unitId, ct);
        if (token is null) return null;

        var rows = await _client.SearchSchedulesAsync(
            token, de, ate, SpineApiClient.ScheduleCategory.Avaliacao, ct);

        var dto = Montar(de, ate, rows);

        _cache.Set(cacheKey, dto, TimeSpan.FromSeconds(_options.CacheSeconds));
        _logger.LogDebug("Spine avaliações {UnitId} {De}→{Ate}: {N} registros", unitId, de, ate, rows.Count);
        return dto;
    }

    internal static SpineAvaliacoesDto Montar(
        DateOnly de, DateOnly ate, IReadOnlyList<SpineSchedule> rows)
    {
        var porStatus = rows.GroupBy(r => r.IdStatus).ToDictionary(g => g.Key, g => g.Count());
        int Contar(int status) => porStatus.GetValueOrDefault(status);

        // Situações conhecidas na ordem definida + qualquer código novo que o Spine
        // passe a devolver (melhor aparecer como desconhecido do que sumir da conta).
        var conhecidos = Situacoes.Select(s => s.Id).ToHashSet();
        var porSituacao = Situacoes
            .Select(s => new SpineSituacaoDto(s.Id, s.Nome, s.Grupo, Contar(s.Id)))
            .Concat(rows.Where(r => !conhecidos.Contains(r.IdStatus))
                        .GroupBy(r => (r.IdStatus, r.StatusName))
                        .Select(g => new SpineSituacaoDto(
                            g.Key.IdStatus, g.Key.StatusName ?? $"Situação {g.Key.IdStatus}",
                            "desconhecido", g.Count())))
            .Where(s => s.Total > 0)
            .ToList();

        var total = rows.Count;
        var realizadas = Contar(SpineApiClient.ScheduleStatus.Atendido);
        var pendentes = Contar(SpineApiClient.ScheduleStatus.Agendado)
                      + Contar(SpineApiClient.ScheduleStatus.Confirmado);

        // Horário que ainda não chegou não é acerto nem erro — fica fora da conta.
        var resolvidas = total - pendentes;
        var taxa = resolvidas == 0 ? 0d : Math.Round((double)realizadas / resolvidas * 100, 1);

        var naoCompareceu = Contar(SpineApiClient.ScheduleStatus.NaoCompareceu);
        var desmarcadas = Contar(SpineApiClient.ScheduleStatus.Desmarcado);
        var alerta = desmarcadas >= 3 && naoCompareceu <= desmarcadas / 5;

        var porDia = rows
            .Where(r => r.DateAttendance.HasValue)
            .GroupBy(r => SpineApiClient.DiaLocal(r.DateAttendance!.Value))
            .OrderBy(g => g.Key)
            .Select(g => new SpineAvaliacoesPorDiaDto(
                g.Key,
                g.Count(),
                g.Count(r => r.IdStatus == SpineApiClient.ScheduleStatus.Atendido)))
            .ToList();

        var porProfissional = rows
            .Where(r => r.IdStatus == SpineApiClient.ScheduleStatus.Atendido)
            .GroupBy(r => (r.PhysicalTherapist ?? "—").Trim())
            .OrderByDescending(g => g.Count())
            .Select(g => new SpineAvaliacoesPorProfissionalDto(g.Key, g.Count()))
            .ToList();

        var pacientes = rows
            .Select(r => (r.ClientName ?? string.Empty).Trim())
            .Where(n => n.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        return new SpineAvaliacoesDto(
            de, ate, total, realizadas, resolvidas, taxa, pacientes, alerta,
            porSituacao, porDia, porProfissional);
    }
}
