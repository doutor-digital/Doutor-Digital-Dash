using LeadAnalytics.Api.DTOs.Spine;
using LeadAnalytics.Api.Options;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace LeadAnalytics.Api.Service.Spine;

/// <summary>
/// Monta o card de Avaliações a partir da agenda do Spine.
///
/// Por que só Avaliação (idCategory=1) e não a agenda inteira: avaliação é a consulta
/// que fecha o funil comercial — é o "compareceu" que hoje o dashboard lê de um campo
/// digitado na Kommo. Sessão (idCategory=2) é tratamento em curso e vira card próprio.
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
    /// O Spine devolve dateAttendance em UTC (guia §9.2). Converter é obrigatório, não
    /// cosmético: existem consultas às 00:15/00:30 UTC que são 21h15/21h30 do dia
    /// ANTERIOR em Imperatriz — agrupar pelo UTC cru joga essas linhas no dia errado.
    /// </summary>
    private static readonly TimeZoneInfo BrTz =
        TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");

    private static DateOnly DiaLocal(DateTime utc) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.SpecifyKind(utc, DateTimeKind.Utc), BrTz));

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
        int Contar(int status) => rows.Count(r => r.IdStatus == status);

        var agendadas = rows.Count;
        var compareceram = Contar(SpineApiClient.ScheduleStatus.Atendido);
        var naoCompareceram = Contar(SpineApiClient.ScheduleStatus.NaoCompareceu);
        var desmarcadas = Contar(SpineApiClient.ScheduleStatus.Desmarcado);
        var remarcadas = Contar(SpineApiClient.ScheduleStatus.Remarcado);
        var aguardando = Contar(SpineApiClient.ScheduleStatus.Agendado)
                       + Contar(SpineApiClient.ScheduleStatus.Confirmado);

        var taxa = agendadas == 0 ? 0d : Math.Round((double)compareceram / agendadas * 100, 1);

        // Sinaliza o vício de preenchimento: desmarque relevante com no-show quase zerado.
        var alerta = desmarcadas >= 3 && naoCompareceram <= desmarcadas / 5;

        var porDia = rows
            .Where(r => r.DateAttendance.HasValue)
            .GroupBy(r => DiaLocal(r.DateAttendance!.Value))
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
            de, ate, agendadas, compareceram, naoCompareceram, desmarcadas, remarcadas,
            aguardando, taxa, pacientes, alerta, porDia, porProfissional);
    }
}
