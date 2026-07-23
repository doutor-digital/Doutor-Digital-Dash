using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using LeadAnalytics.Api.Options;
using Microsoft.Extensions.Options;

namespace LeadAnalytics.Api.Service.Spine;

/// <summary>
/// Cliente da API Spine (sistema clínico do Doutor Hérnia).
/// Somente leitura — o Spine é dono do dado operacional, a gente só consulta.
///
/// Diferenças entre o guia de integração (v1.9.3) e a API real (v1.9.6), medidas em
/// 23/07/2026 contra produção. O parser abaixo segue a API real:
///   • envelope é {"status":"success","data":{...}} e não {"success":true,...};
///   • em /search o payload fica em data.data + data.total/page/totalPages;
///   • em /general o data é o array direto;
///   • /leads/search exige initialDate+endDate (o guia diz opcional → 400 sem elas);
///   • em /schedules/search as datas filtram dateAttendance, não a data de criação;
///   • /schedules/search ACEITA E IGNORA em silêncio idClient, idStatus e idTreatment
///     (o total não muda) — por isso este client só manda os filtros que funcionam
///     de fato: initialDate, endDate, name, idCategory e pagination.
/// </summary>
public class SpineApiClient
{
    /// <summary>Status de agendamento. Não existe endpoint que liste isso —
    /// mapa levantado por amostragem de 1.288 agendamentos da unidade 133.</summary>
    public static class ScheduleStatus
    {
        public const int Agendado = 37;
        public const int Confirmado = 38;
        public const int NaoCompareceu = 40;
        public const int Remarcado = 41;
        public const int Atendido = 42;
        public const int Desmarcado = 57;
    }

    /// <summary>Categorias de agenda (GET /api/general/schedules/categories).</summary>
    public static class ScheduleCategory
    {
        public const int Avaliacao = 1;
        public const int Sessao = 2;
        public const int Retorno = 3;
        public const int RetornoComExames = 6;
        public const int RetornoAposTratamento = 7;
    }

    /// <summary>Máximo aceito pela API; acima disso ela devolve 400.</summary>
    public const int MaxRowsPerPage = 100;

    /// <summary>
    /// Janela máxima da API. 100 dias é o limite deles; como pedimos sempre um dia
    /// a mais (ver <see cref="SearchSchedulesAsync"/>), o teto efetivo aqui é 99.
    /// </summary>
    public const int MaxDiasJanela = 99;

    /// <summary>
    /// O Spine devolve dateAttendance em UTC (guia §9.2). Imperatriz é UTC−3 e não
    /// tem horário de verão desde 2019, mas usamos o fuso nomeado por consistência
    /// com o resto do projeto (ContactImportService, DailyRelatoryService).
    /// </summary>
    public static readonly TimeZoneInfo BrTz = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");

    /// <summary>Data local do atendimento — é por ela que se agrupa, nunca pelo UTC cru.</summary>
    public static DateOnly DiaLocal(DateTime utc) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.SpecifyKind(utc, DateTimeKind.Utc), BrTz));

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _http;
    private readonly SpineOptions _options;
    private readonly ILogger<SpineApiClient> _logger;

    public SpineApiClient(HttpClient http, IOptions<SpineOptions> options, ILogger<SpineApiClient> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>Healthcheck (não exige token).</summary>
    public async Task<bool> IsUpAsync(CancellationToken ct = default)
    {
        try
        {
            var res = await _http.GetAsync($"{Base}/check", ct);
            return res.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Spine /check falhou");
            return false;
        }
    }

    /// <summary>
    /// Busca agendamentos por janela de <b>data de atendimento</b>, paginando até o fim.
    ///
    /// MEDIDO NA API (23/07/2026): <c>initialDate</c> é inclusivo e <c>endDate</c> é
    /// EXCLUSIVO — pedir 01→23/07 devolve até 22/07 e some com o dia inteiro do 23.
    /// Por isso pedimos <c>to + 1 dia</c> e recortamos aqui pela data LOCAL. Sem isso
    /// todo período perde o último dia, que costuma ser justamente o de hoje.
    /// </summary>
    /// <param name="idCategory">1=Avaliação, 2=Sessão… null traz todas.</param>
    public async Task<IReadOnlyList<SpineSchedule>> SearchSchedulesAsync(
        string token, DateOnly from, DateOnly to, int? idCategory, CancellationToken ct = default)
    {
        if (to < from) (from, to) = (to, from);
        if (to.DayNumber - from.DayNumber > MaxDiasJanela)
            throw new ArgumentException(
                $"Spine aceita no máximo {MaxDiasJanela} dias por consulta.", nameof(to));

        var endDatePedido = to.AddDays(1);

        var all = new List<SpineSchedule>();
        var page = 1;
        var totalPages = 1;

        // Guarda-chuva: a unidade 133 fez ~350 agendamentos/mês. 40 páginas de 100
        // cobrem o pior caso com folga e evitam loop infinito se a paginação regredir.
        while (page <= totalPages && page <= 40)
        {
            var body = new Dictionary<string, object?>
            {
                ["initialDate"] = from.ToString("yyyy-MM-dd"),
                ["endDate"] = endDatePedido.ToString("yyyy-MM-dd"),
                ["pagination"] = new { page, rowsPerPage = MaxRowsPerPage },
            };
            if (idCategory.HasValue) body["idCategory"] = idCategory.Value;

            var envelope = await PostAsync<SpineSearchEnvelope<SpineSchedule>>(
                "/api/schedules/search", body, token, ct);

            var rows = envelope?.Data?.Data;
            if (rows is null || rows.Count == 0) break;

            all.AddRange(rows);
            totalPages = envelope!.Data!.TotalPages ?? 1;
            page++;
        }

        // Recorta o dia extra que pedimos: fica só o que cai na janela em horário local.
        return all
            .Where(r => r.DateAttendance is null
                        || (DiaLocal(r.DateAttendance.Value) >= from
                            && DiaLocal(r.DateAttendance.Value) <= to))
            .ToList();
    }

    private string Base => _options.BaseUrl.TrimEnd('/');

    private async Task<T?> PostAsync<T>(string path, object body, string token, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, $"{Base}{path}")
        {
            Content = new StringContent(JsonSerializer.Serialize(body, JsonOpts), Encoding.UTF8, "application/json"),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var res = await _http.SendAsync(req, ct);
        var payload = await res.Content.ReadAsStringAsync(ct);

        if (!res.IsSuccessStatusCode)
        {
            // 403 = módulo não liberado no token da unidade (Tratamentos, Finanças e BI
            // seguem bloqueados). Vale distinguir de 401 (token inválido/revogado).
            var motivo = res.StatusCode switch
            {
                HttpStatusCode.Unauthorized => "token inválido, ausente ou revogado",
                HttpStatusCode.Forbidden => "módulo não liberado no token desta unidade",
                HttpStatusCode.BadRequest => "parâmetros inválidos",
                _ => "erro na API Spine",
            };
            _logger.LogWarning("Spine {Path} → {Status} ({Motivo}): {Payload}",
                path, (int)res.StatusCode, motivo, Truncate(payload, 300));
            throw new SpineApiException(res.StatusCode, motivo, Truncate(payload, 300));
        }

        return JsonSerializer.Deserialize<T>(payload, JsonOpts);
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "…";
}

/// <summary>Falha de chamada à API Spine, já traduzida.</summary>
public class SpineApiException(HttpStatusCode status, string motivo, string payload)
    : Exception($"Spine respondeu {(int)status}: {motivo}")
{
    public HttpStatusCode Status { get; } = status;
    public string Motivo { get; } = motivo;
    public string Payload { get; } = payload;
}

/// <summary>Envelope real de /search: {"status":"success","data":{"data":[…],"total":N}}.</summary>
public class SpineSearchEnvelope<T>
{
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("data")] public SpineSearchPage<T>? Data { get; set; }
}

public class SpineSearchPage<T>
{
    [JsonPropertyName("data")] public List<T>? Data { get; set; }
    [JsonPropertyName("total")] public int? Total { get; set; }
    [JsonPropertyName("page")] public int? Page { get; set; }
    [JsonPropertyName("rowsPerPage")] public int? RowsPerPage { get; set; }
    [JsonPropertyName("totalPages")] public int? TotalPages { get; set; }
}

/// <summary>
/// Linha de /api/schedules/search. Note a ausência de idClient: a agenda traz só o
/// nome do paciente como texto, o que impede ligar agendamento → paciente por aqui.
/// </summary>
public class SpineSchedule
{
    [JsonPropertyName("idSchedule")] public long IdSchedule { get; set; }
    [JsonPropertyName("idTreatment")] public long? IdTreatment { get; set; }
    [JsonPropertyName("clientName")] public string? ClientName { get; set; }
    [JsonPropertyName("dateAttendance")] public DateTime? DateAttendance { get; set; }
    [JsonPropertyName("physicalTherapist")] public string? PhysicalTherapist { get; set; }
    [JsonPropertyName("idStatus")] public int IdStatus { get; set; }
    [JsonPropertyName("statusName")] public string? StatusName { get; set; }
    [JsonPropertyName("modified")] public DateTime? Modified { get; set; }
    [JsonPropertyName("modifiedBy")] public string? ModifiedBy { get; set; }
}
