using LeadAnalytics.Api.DTOs.Spine;
using LeadAnalytics.Api.Options;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace LeadAnalytics.Api.Service.Spine;

/// <summary>
/// Agenda da clínica para a visão de calendário.
///
/// Faz uma requisição por categoria e carimba o resultado, porque
/// /schedules/search não devolve a categoria do horário — só aceita filtrar por
/// ela. É o mesmo truque usado no workflow n8n de histórico.
/// </summary>
public class SpineAgendaService(
    SpineApiClient client,
    SpineTokenStore tokens,
    IMemoryCache cache,
    IOptions<SpineOptions> options,
    ILogger<SpineAgendaService> logger)
{
    private readonly SpineApiClient _client = client;
    private readonly SpineTokenStore _tokens = tokens;
    private readonly IMemoryCache _cache = cache;
    private readonly SpineOptions _options = options.Value;
    private readonly ILogger<SpineAgendaService> _logger = logger;

    /// <summary>De GET /api/general/schedules/categories, na ordem de leitura da agenda.</summary>
    private static readonly (int Id, string Nome)[] Categorias =
    [
        (SpineApiClient.ScheduleCategory.Avaliacao, "Avaliação"),
        (SpineApiClient.ScheduleCategory.Sessao, "Sessão"),
        (SpineApiClient.ScheduleCategory.Retorno, "Retorno"),
        (SpineApiClient.ScheduleCategory.RetornoComExames, "Retorno com exames"),
        (SpineApiClient.ScheduleCategory.RetornoAposTratamento, "Retorno após tratamento"),
    ];

    private static string Grupo(int idStatus) => idStatus switch
    {
        SpineApiClient.ScheduleStatus.Atendido => "realizado",
        SpineApiClient.ScheduleStatus.NaoCompareceu => "falta",
        SpineApiClient.ScheduleStatus.Desmarcado or SpineApiClient.ScheduleStatus.Remarcado => "cancelado",
        SpineApiClient.ScheduleStatus.Agendado or SpineApiClient.ScheduleStatus.Confirmado => "pendente",
        _ => "desconhecido",
    };

    public async Task<SpineAgendaDto?> GetAsync(
        int unitId, DateOnly de, DateOnly ate, CancellationToken ct = default)
    {
        var cacheKey = $"spine:agenda:{unitId}:{de:yyyyMMdd}:{ate:yyyyMMdd}";
        if (_cache.TryGetValue<SpineAgendaDto>(cacheKey, out var hit) && hit is not null)
            return hit;

        var token = await _tokens.GetTokenAsync(unitId, ct);
        if (token is null) return null;

        var itens = new List<SpineAgendaItemDto>();

        foreach (var (idCat, nome) in Categorias)
        {
            var rows = await _client.SearchSchedulesAsync(token, de, ate, idCat, ct);
            foreach (var r in rows)
            {
                if (r.DateAttendance is null) continue;

                var local = TimeZoneInfo.ConvertTimeFromUtc(
                    DateTime.SpecifyKind(r.DateAttendance.Value, DateTimeKind.Utc), SpineApiClient.BrTz);

                itens.Add(new SpineAgendaItemDto(
                    r.IdSchedule,
                    r.IdTreatment,
                    (r.ClientName ?? "—").Trim(),
                    local,
                    idCat,
                    nome,
                    (r.PhysicalTherapist ?? "").Trim(),
                    r.IdStatus,
                    r.StatusName ?? $"Situação {r.IdStatus}",
                    Grupo(r.IdStatus)));
            }
        }

        var dto = new SpineAgendaDto(
            de, ate, itens.Count,
            itens.Select(i => i.Categoria).Distinct().ToList(),
            itens.OrderBy(i => i.Inicio).ToList());

        _cache.Set(cacheKey, dto, TimeSpan.FromSeconds(_options.CacheSeconds));
        _logger.LogDebug("Spine agenda {UnitId} {De}→{Ate}: {N} horários", unitId, de, ate, itens.Count);
        return dto;
    }
}
