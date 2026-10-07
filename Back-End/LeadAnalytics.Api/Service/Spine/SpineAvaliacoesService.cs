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

    /// <summary>
    /// Maior período (em dias, contando o primeiro e o último) que o painel lê da agenda
    /// de uma vez. A franquia só responde 100 dias por consulta, então o período é fatiado
    /// (<see cref="BlocosDaAgenda"/>); 400 dias = no máximo 4 fatias por categoria, o que
    /// cobre a pílula "Ano" (366 dias). Acima disso o painel NÃO pede: um período de 3 anos
    /// viraria 11 fatias × 5 categorias × páginas por unidade, e a franquia já pediu (06/10)
    /// que o consumo da API caia, não suba.
    /// </summary>
    public const int MaxDiasAgendaKpi = 400;

    /// <summary>
    /// Quebra o período em fatias que a agenda da franquia aceita: cada uma com no máximo
    /// <see cref="SpineApiClient.MaxDiasJanela"/> dias de diferença entre o início e o fim
    /// (100 dias contando os dois), encostadas e sem sobreposição — o dia em que uma acaba
    /// não se repete na seguinte, então nenhum horário é contado duas vezes.
    /// </summary>
    internal static IEnumerable<(DateOnly De, DateOnly Ate)> BlocosDaAgenda(DateOnly de, DateOnly ate)
    {
        if (ate < de) (de, ate) = (ate, de);
        var cursor = de;
        while (cursor <= ate)
        {
            var fim = cursor.AddDays(SpineApiClient.MaxDiasJanela);
            if (fim > ate) fim = ate;
            yield return (cursor, fim);
            cursor = fim.AddDays(1);
        }
    }

    /// <summary>Quantos dias o período tem, contando o primeiro e o último.</summary>
    public static int DiasNoPeriodo(DateOnly de, DateOnly ate) => Math.Abs(ate.DayNumber - de.DayNumber) + 1;

    /// <summary>Card de Avaliações (idCategory=1). Atalho para o caso mais comum.</summary>
    public Task<SpineAvaliacoesDto?> GetAsync(
        int unitId, DateOnly de, DateOnly ate, CancellationToken ct = default) =>
        GetPorCategoriasAsync(unitId, de, ate, [SpineApiClient.ScheduleCategory.Avaliacao], ct);

    /// <summary>
    /// Mesmo cálculo do card de avaliações, para QUALQUER conjunto de categorias.
    /// Serve sessões (adesão ao tratamento), retornos etc. sem duplicar lógica.
    /// A agenda não devolve a categoria por linha, então puxa uma por categoria e
    /// junta, dedup por idSchedule (um horário pode aparecer em duas por engano).
    /// </summary>
    public async Task<SpineAvaliacoesDto?> GetPorCategoriasAsync(
        int unitId, DateOnly de, DateOnly ate, int[] idCategorias, CancellationToken ct = default)
    {
        // Teto ANTES do cache e do token: período longo demais nunca vira chamada à franquia.
        // Quem chama decide o que mostrar (o KPI mostra "—" com o motivo).
        if (DiasNoPeriodo(de, ate) > MaxDiasAgendaKpi)
            throw new ArgumentException(
                $"O painel lê no máximo {MaxDiasAgendaKpi} dias da agenda da franquia por vez.", nameof(ate));

        var chaveCat = string.Join("-", idCategorias.OrderBy(x => x));
        var cacheKey = $"spine:cat:{unitId}:{chaveCat}:{de:yyyyMMdd}:{ate:yyyyMMdd}";
        if (_cache.TryGetValue<SpineAvaliacoesDto>(cacheKey, out var hit) && hit is not null)
            return hit;

        var token = await _tokens.GetTokenAsync(unitId, ct);
        if (token is null) return null;

        // A agenda só aceita 100 dias por consulta. Antes, um período maior (pílula "Ano")
        // estourava aqui, a exceção virava "franquia indisponível" e o card publicava 0 —
        // Agendados, Consultas e No-show zerados no ano inteiro. Agora o período é fatiado
        // em blocos encostados e somado; o dedup por idSchedule continua de guarda (horário
        // sem data volta em toda fatia e só pode contar uma vez).
        var vistos = new HashSet<long>();
        var rows = new List<SpineSchedule>();
        foreach (var idCat in idCategorias)
        {
            foreach (var (blocoDe, blocoAte) in BlocosDaAgenda(de, ate))
            {
                var parte = await _client.SearchSchedulesAsync(token, blocoDe, blocoAte, idCat, ct);
                foreach (var r in parte)
                    if (vistos.Add(r.IdSchedule)) rows.Add(r);
            }
        }

        var dto = Montar(de, ate, rows);
        _cache.Set(cacheKey, dto, TimeSpan.FromSeconds(_options.CacheSeconds));
        _logger.LogDebug("Spine cat {Cats} {UnitId} {De}→{Ate}: {N} registros", chaveCat, unitId, de, ate, rows.Count);
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

        // Falta disfarçada de desmarque: a baixa entrou DEPOIS da hora do atendimento.
        // Ninguém desmarca uma consulta que já passou — é a recepção fechando o horário
        // porque o paciente não veio.
        var desmarcadoTarde = rows.Count(r =>
            r.IdStatus == SpineApiClient.ScheduleStatus.Desmarcado
            && r.Modified is not null && r.DateAttendance is not null
            && r.Modified >= r.DateAttendance);
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
            de, ate, total, realizadas, resolvidas, taxa, pacientes, alerta, desmarcadoTarde,
            porSituacao, porDia, porProfissional);
    }
}
