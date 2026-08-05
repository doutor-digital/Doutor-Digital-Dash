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
///
/// Leitura, mais DUAS escritas e só elas: confirmar e cancelar agendamento. O Spine é dono
/// do dado operacional, e criar paciente/lead/agendamento é competência do agente-dt — que
/// já tem trava de unique contra o retry do webhook. Um segundo caminho de escrita para o
/// mesmo CRM criaria duplicata, e a API da franquia NÃO tem exclusão de lead (404): cada
/// duplicata é permanente e só some na mão.
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
    /// Janela dos endpoints de BI. Aqui as datas vão exatamente como pedidas — não há o
    /// dia extra da agenda —, então o teto é o do guia: 100 dias.
    /// </summary>
    public const int MaxDiasJanelaBi = 100;

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
        // A API da franquia mistura os tipos: `price` vem como "3680.00" (texto) enquanto
        // outros números vêm como número. Sem essa tolerância, um único campo assim
        // derruba a resposta inteira por exceção de desserialização — e o efeito visível
        // não é erro na tela, é o dashboard caindo em silêncio para a fonte reserva com
        // outro número.
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    /// <summary>Uma vez por processo: só interessa descobrir o formato, não poluir o log.</summary>
    private bool _camposTratamentoRegistrados;

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

    /// <summary>
    /// Valida um token fazendo a consulta mais barata possível (1 dia de agenda).
    /// Usado no onboarding: distingue token válido de 401 (revogado) e 403 (módulo
    /// não liberado), devolvendo o motivo em vez de estourar.
    /// </summary>
    public async Task<(bool Ok, string? Motivo)> ValidateTokenAsync(string token, CancellationToken ct = default)
    {
        var hoje = DateOnly.FromDateTime(DateTime.UtcNow);
        try
        {
            await SearchSchedulesAsync(token, hoje, hoje, ScheduleCategory.Avaliacao, ct);
            return (true, null);
        }
        catch (SpineApiException ex)
        {
            return (false, ex.Motivo);
        }
    }

    /// <summary>
    /// Busca clientes por nome (parcial, case-insensitive). A agenda só traz o nome
    /// do paciente como texto — é por aqui que se resolve nome → idClient para abrir
    /// a ficha. Mín. 2 caracteres; devolve a lista resumida.
    /// </summary>
    public async Task<IReadOnlyList<SpineClientRow>> SearchClientsByNameAsync(
        string token, string name, CancellationToken ct = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["name"] = name,
            ["pagination"] = new { page = 1, rowsPerPage = MaxRowsPerPage },
        };
        var env = await PostAsync<SpineSearchEnvelope<SpineClientRow>>("/api/clients/search", body, token, ct);
        return env?.Data?.Data ?? [];
    }

    /// <summary>
    /// Ficha completa do paciente pelo id, com histórico de agendamentos embutido
    /// (<c>schedules[]</c>). É a rota mais rica do token — traz protocolo e situação
    /// de cada sessão sem depender do módulo Tratamentos (bloqueado por permissão).
    /// Retorna null quando o id não existe ({"success":false,"data":null}).
    /// </summary>
    public async Task<SpineClientDetail?> GetClientAsync(string token, long idClient, CancellationToken ct = default)
    {
        var env = await GetAsync<SpineDetailEnvelope<SpineClientDetail>>($"/api/clients/{idClient}", token, ct);
        return env?.Data?.Data;
    }

    /// <summary>
    /// Tratamentos da unidade pela rota oficial (<c>POST /api/treatments/search</c>).
    ///
    /// PARECIA QUE A API IGNORAVA O FILTRO DE DATA. Não ignorava: recebia os nomes
    /// errados. Esta rota filtra por <c>initialCreatedDate</c>/<c>endCreatedDate</c>,
    /// e não por <c>initialDate</c>/<c>endDate</c> como a agenda. Parâmetro desconhecido
    /// não vira erro — a API cai no padrão dela, o mês corrente (guia §10.4), e o card
    /// ficava preso no mesmo número em qualquer período.
    ///
    /// A janela é quebrada em blocos de 100 dias (teto do guia para busca com período),
    /// já que o card de tratamentos pede 365 dias por padrão.
    /// </summary>
    public async Task<IReadOnlyList<SpineTreatment>> SearchTreatmentsAsync(
        string token, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        if (to < from) (from, to) = (to, from);

        // Dedup por id: uma janela grande vira vários blocos, e bloco vizinho pode
        // devolver a mesma linha na borda.
        var porId = new Dictionary<long, SpineTreatment>();

        foreach (var (blocoDe, blocoAte) in QuebrarEmBlocos(from, to, MaxDiasJanelaBi))
        {
            var page = 1;
            var totalPages = 1;

            while (page <= totalPages && page <= 40)
            {
                // OS NOMES SÃO OUTROS AQUI, E ISSO CUSTOU CARO.
                // /treatments/search filtra por initialCreatedDate/endCreatedDate — não
                // por initialDate/endDate, que são os nomes da agenda. Mandar os nomes
                // errados não dá erro: a API ignora o que não conhece e cai no padrão
                // dela, que é o MÊS CORRENTE (guia §10.4). O efeito visível era o card
                // preso no mesmo número em qualquer período escolhido.
                var body = new Dictionary<string, object?>
                {
                    ["initialCreatedDate"] = blocoDe.ToString("yyyy-MM-dd"),
                    // MEDIDO (04/08/2026): aqui o fim é INCLUSIVO — o oposto de
                    // /schedules/search, onde é exclusivo. Pedir 01/07→01/08 devolveu as
                    // duas linhas do próprio 01/08.
                    //
                    // Mesmo assim pedimos um dia a mais, de propósito: `created` vem em UTC
                    // e o corte é pelo dia da clínica. Quem lançou às 22h de 31/07 chega
                    // como 01/08T01:00Z e o servidor não devolveria essa linha num pedido
                    // que termina em 31/07. O dia extra entra e o corte local devolve.
                    ["endCreatedDate"] = blocoAte.AddDays(1).ToString("yyyy-MM-dd"),
                    ["pagination"] = new { page, rowsPerPage = MaxRowsPerPage },
                };

                // Desserializa em JsonElement antes do tipo: é o que permite registrar os
                // campos que a rota REALMENTE devolve. O mapa nosso foi escrito a partir
                // do guia, e o guia não documenta a resposta desta rota — foi assim que
                // `created` virou nulo silencioso e derrubou o filtro.
                var envelope = await PostAsync<SpineSearchEnvelope<JsonElement>>(
                    "/api/treatments/search", body, token, ct);

                var rows = envelope?.Data?.Data;
                if (rows is null || rows.Count == 0) break;

                if (!_camposTratamentoRegistrados && rows[0].ValueKind == JsonValueKind.Object)
                {
                    _camposTratamentoRegistrados = true;
                    var campos = rows[0].EnumerateObject().Select(p => $"{p.Name}:{p.Value.ValueKind}");
                    _logger.LogInformation(
                        "Spine /treatments/search devolve os campos: {Campos}", string.Join(", ", campos));
                }

                foreach (var el in rows)
                {
                    var t = el.Deserialize<SpineTreatment>(JsonOpts);
                    if (t is not null) porId[t.IdTreatment] = t;
                }

                totalPages = envelope!.Data!.TotalPages ?? 1;
                page++;
            }
        }

        var brutos = porId.Values.ToList();

        // `created` vem em UTC. Um tratamento lançado às 22h de 31/07 (BRT) chega como
        // 01/08T01:00Z e cairia no mês seguinte se comparado cru — o mesmo erro de fuso
        // que a agenda já trata.
        bool NoPeriodo(DateTime? d) =>
            d is not null && DiaLocal(d.Value) >= from && DiaLocal(d.Value) <= to;

        // Corte fino por data de LANÇAMENTO (created) — o eixo que o card anuncia e o
        // mesmo que a tela da franquia usa.
        //
        // A versão anterior deixava passar linha SEM data ("o servidor já filtrou, então
        // confio"). Não filtrou: a rota devolveu a base inteira e o card foi de 2 para
        // 184 num mês em que a franquia mostra 10. Confiar no servidor só é seguro
        // quando dá para verificar que ele filtrou, e aqui não dá — então exige-se data.
        var filtrados = brutos.Where(t => NoPeriodo(t.Created)).ToList();

        // Os dois eixos lado a lado no log: é o que permite dizer, sem adivinhar, qual
        // deles bate com o número que a clínica vê na tela da franquia.
        _logger.LogInformation(
            "Spine tratamentos {De}..{Ate}: brutos={Brutos} comCreated={ComCreated} comDateBegin={ComBegin} "
            + "noPeriodoPorCreated={PorCreated} noPeriodoPorDateBegin={PorBegin}",
            from, to, brutos.Count,
            brutos.Count(t => t.Created is not null),
            brutos.Count(t => t.DateBegin is not null),
            filtrados.Count,
            brutos.Count(t => NoPeriodo(t.DateBegin)));

        return filtrados;
    }

    /// <summary>
    /// Confirma a presença em um agendamento (<c>PATCH /api/schedules/confirm</c>).
    ///
    /// É o par do botão "Confirmar presença" dos templates de WhatsApp: sem esta chamada
    /// o paciente clica e a resposta morre no chat, com a recepção confirmando por telefone
    /// do mesmo jeito. A API define <c>SCHEDULE_CONFIRMED</c> e carimba <c>notificationAt</c>.
    /// </summary>
    public async Task<bool> ConfirmScheduleAsync(string token, long idSchedule, CancellationToken ct = default)
    {
        var env = await SendJsonAsync<SpineWriteEnvelope>(
            HttpMethod.Patch, "/api/schedules/confirm", new { idSchedule }, token, ct);
        return env?.Ok ?? false;
    }

    /// <summary>
    /// Cancela um agendamento (<c>DELETE /api/schedules</c>) — par do botão "Preciso remarcar".
    ///
    /// A API marca o agendamento como <c>DELETED</c>; ela NÃO remarca. Remarcar é cancelar
    /// e criar outro, e criar é competência do agente-dt — este cliente só cancela, para não
    /// abrir um segundo caminho de escrita para o mesmo CRM.
    /// </summary>
    public async Task<bool> CancelScheduleAsync(string token, long idSchedule, CancellationToken ct = default)
    {
        var env = await SendJsonAsync<SpineWriteEnvelope>(
            HttpMethod.Delete, "/api/schedules", new { idSchedule }, token, ct);
        return env?.Ok ?? false;
    }

    /// <summary>
    /// Leads por origem, pelo número oficial da franquia (<c>POST /api/bi/leads/sources</c>).
    ///
    /// Serve de contraponto ao campo <c>⚑ Origem</c> do Kommo, que depende de a SDR digitar
    /// — e que está preenchido em ~30% dos leads. Dois números comparáveis valem mais do que
    /// um número nosso sozinho numa conversa com a franqueada.
    ///
    /// DUAS RESTRIÇÕES DO GUIA, E POR QUE ELAS APARECEM AQUI
    ///   • <c>initialDate</c>/<c>endDate</c> são obrigatórios e a janela máxima é de 100 dias.
    ///     O filtro do dashboard aceita períodos maiores, então a janela é quebrada em blocos
    ///     e os totais somados — sem isso um trimestre devolveria 400.
    ///   • a franquia pede consulta 1–2× ao dia. Quem chama isto deve vir por cache/job,
    ///     nunca a cada clique de filtro.
    ///
    /// AINDA NÃO VALIDADO CONTRA A API: o módulo BI segue bloqueado nos tokens das unidades
    /// (403). O formato abaixo veio do guia de integração v3, não de resposta observada.
    /// </summary>
    public async Task<IReadOnlyList<SpineLeadSource>> GetLeadSourcesAsync(
        string token, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        if (to < from) (from, to) = (to, from);

        // Soma por nome de origem: blocos diferentes trazem a mesma origem repetida.
        var acumulado = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var (blocoDe, blocoAte) in QuebrarEmBlocos(from, to, MaxDiasJanelaBi))
        {
            var body = new
            {
                initialDate = blocoDe.ToString("yyyy-MM-dd"),
                endDate = blocoAte.ToString("yyyy-MM-dd"),
            };

            var env = await PostAsync<SpineBiEnvelope<SpineLeadSourcesData>>(
                "/api/bi/leads/sources", body, token, ct);

            // 200 com corpo que não entendemos é o pior caso: devolveria "nenhuma origem"
            // como se a franquia não tivesse leads no período. Como o formato do BI nunca
            // foi observado (módulo bloqueado no token), isso precisa aparecer no log.
            if (env?.Data?.Sources is null)
            {
                _logger.LogWarning(
                    "Spine BI /leads/sources respondeu 200 sem 'sources' para {De}→{Ate}. "
                    + "Formato provavelmente diferente do guia — conferir antes de confiar no número.",
                    blocoDe, blocoAte);
            }

            foreach (var row in env?.Data?.Sources ?? [])
            {
                var nome = string.IsNullOrWhiteSpace(row.SourceName) ? "Sem origem" : row.SourceName.Trim();
                acumulado[nome] = acumulado.GetValueOrDefault(nome) + row.Total;
            }
        }

        return acumulado
            .Select(kv => new SpineLeadSource { SourceName = kv.Key, Total = kv.Value })
            .OrderByDescending(s => s.Total)
            .ToList();
    }

    /// <summary>
    /// Fatia [de, ate] em blocos de no máximo <paramref name="maxDias"/> dias, inclusivo nas pontas.
    /// </summary>
    private static IEnumerable<(DateOnly De, DateOnly Ate)> QuebrarEmBlocos(DateOnly de, DateOnly ate, int maxDias)
    {
        var cursor = de;
        while (cursor <= ate)
        {
            var fim = cursor.AddDays(maxDias - 1);
            if (fim > ate) fim = ate;
            yield return (cursor, fim);
            cursor = fim.AddDays(1);
        }
    }

    private string Base => _options.BaseUrl.TrimEnd('/');

    /// <summary>
    /// PATCH/DELETE com corpo JSON. O <c>DELETE</c> da Spine leva <c>idSchedule</c> no body,
    /// e não na query — <c>HttpClient.DeleteAsync</c> não serve.
    /// </summary>
    private async Task<T?> SendJsonAsync<T>(
        HttpMethod metodo, string path, object body, string token, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(metodo, $"{Base}{path}")
        {
            Content = new StringContent(JsonSerializer.Serialize(body, JsonOpts), Encoding.UTF8, "application/json"),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var res = await _http.SendAsync(req, ct);
        var payload = await res.Content.ReadAsStringAsync(ct);

        if (!res.IsSuccessStatusCode)
        {
            var motivo = res.StatusCode switch
            {
                HttpStatusCode.Unauthorized => "token inválido, ausente ou revogado",
                HttpStatusCode.Forbidden => "módulo não liberado no token desta unidade",
                HttpStatusCode.NotFound => "agendamento não encontrado",
                HttpStatusCode.BadRequest => "parâmetros inválidos",
                _ => "erro na API Spine",
            };
            _logger.LogWarning("Spine {Metodo} {Path} → {Status} ({Motivo}): {Payload}",
                metodo.Method, path, (int)res.StatusCode, motivo, Truncate(payload, 300));
            throw new SpineApiException(res.StatusCode, motivo, Truncate(payload, 300));
        }

        return JsonSerializer.Deserialize<T>(payload, JsonOpts);
    }

    private async Task<T?> GetAsync<T>(string path, string token, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, $"{Base}{path}");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var res = await _http.SendAsync(req, ct);
        var payload = await res.Content.ReadAsStringAsync(ct);

        if (!res.IsSuccessStatusCode)
        {
            var motivo = res.StatusCode switch
            {
                HttpStatusCode.Unauthorized => "token inválido, ausente ou revogado",
                HttpStatusCode.Forbidden => "módulo não liberado no token desta unidade",
                HttpStatusCode.NotFound => "recurso não encontrado",
                _ => "erro na API Spine",
            };
            _logger.LogWarning("Spine GET {Path} → {Status} ({Motivo}): {Payload}",
                path, (int)res.StatusCode, motivo, Truncate(payload, 300));
            throw new SpineApiException(res.StatusCode, motivo, Truncate(payload, 300));
        }

        return JsonSerializer.Deserialize<T>(payload, JsonOpts);
    }

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

/// <summary>Envelope de GET por id: {"status":"success","data":{"data":{…}}}.</summary>
public class SpineDetailEnvelope<T>
{
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("data")] public SpineDetailWrap<T>? Data { get; set; }
}

public class SpineDetailWrap<T>
{
    [JsonPropertyName("data")] public T? Data { get; set; }
}

/// <summary>Linha resumida de /api/clients/search.</summary>
public class SpineClientRow
{
    [JsonPropertyName("idClient")] public long IdClient { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("whatsapp")] public string? Whatsapp { get; set; }
    [JsonPropertyName("addressCity")] public string? AddressCity { get; set; }
    [JsonPropertyName("addressUf")] public string? AddressUf { get; set; }
    [JsonPropertyName("sourceName")] public string? SourceName { get; set; }
    [JsonPropertyName("statusName")] public string? StatusName { get; set; }
    [JsonPropertyName("created")] public DateTime? Created { get; set; }
}

/// <summary>Ficha de /api/clients/{id}, com agenda embutida.</summary>
public class SpineClientDetail
{
    [JsonPropertyName("idClient")] public long IdClient { get; set; }
    [JsonPropertyName("source")] public string? Source { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("birthdate")] public DateTime? Birthdate { get; set; }
    [JsonPropertyName("gender")] public string? Gender { get; set; }
    [JsonPropertyName("address")] public string? Address { get; set; }
    [JsonPropertyName("addressNumber")] public string? AddressNumber { get; set; }
    [JsonPropertyName("addressCity")] public string? AddressCity { get; set; }
    [JsonPropertyName("addressUf")] public string? AddressUf { get; set; }
    [JsonPropertyName("email")] public string? Email { get; set; }
    [JsonPropertyName("whatsapp")] public string? Whatsapp { get; set; }
    [JsonPropertyName("schedules")] public List<SpineClientSchedule>? Schedules { get; set; }
}

/// <summary>Item do histórico embutido na ficha (traz a categoria/protocolo por extenso).</summary>
public class SpineClientSchedule
{
    [JsonPropertyName("idSchedule")] public long IdSchedule { get; set; }
    [JsonPropertyName("dateAttendance")] public DateTime? DateAttendance { get; set; }
    [JsonPropertyName("category")] public string? Category { get; set; }
    [JsonPropertyName("physicalTherapist")] public string? PhysicalTherapist { get; set; }
    [JsonPropertyName("idStatus")] public int IdStatus { get; set; }
    [JsonPropertyName("statusName")] public string? StatusName { get; set; }
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

/// <summary>
/// Um tratamento como a API oficial devolve (<c>/api/treatments/search</c>).
///
/// Mais rico que o export raspado do CRM web: traz categoria, local, grau,
/// profissional e preço em campo próprio, sem parse de planilha. O que ele NÃO traz é
/// a situação financeira (pago/pendente) — essa só existe no export.
/// </summary>
public class SpineTreatment
{
    [JsonPropertyName("idTreatment")] public long IdTreatment { get; set; }

    // ─── paciente ───────────────────────────────────────────────────────────
    [JsonPropertyName("idClient")] public long IdClient { get; set; }
    [JsonPropertyName("clientName")] public string? ClientName { get; set; }
    [JsonPropertyName("clientBirthdate")] public DateTime? ClientBirthdate { get; set; }

    /// <summary>M · F.</summary>
    [JsonPropertyName("clientGender")] public string? ClientGender { get; set; }

    // ─── protocolo ──────────────────────────────────────────────────────────
    // O guia diz que idCategory é 2=Fisioterapia e 4=Cirurgia. Não é o que a conta
    // devolve: em Imperatriz, 4 = "PROTOCOLO 03 MESES". A categoria é por unidade,
    // então agrupe pelo id e mostre o nome — nunca chumbe o significado do id.
    [JsonPropertyName("idCategory")] public int? IdCategory { get; set; }
    [JsonPropertyName("category")] public string? Category { get; set; }
    [JsonPropertyName("idLocal")] public int? IdLocal { get; set; }

    /// <summary>LOMBAR · CERVICAL…</summary>
    [JsonPropertyName("local")] public string? Local { get; set; }

    [JsonPropertyName("idType")] public int? IdType { get; set; }

    /// <summary>Vem "N/A" quando idType é 0.</summary>
    [JsonPropertyName("type")] public string? Type { get; set; }

    [JsonPropertyName("idDegree")] public int? IdDegree { get; set; }

    /// <summary>CRÔNICO · AGUDO…</summary>
    [JsonPropertyName("degree")] public string? Degree { get; set; }

    // ─── responsável e situação ─────────────────────────────────────────────
    [JsonPropertyName("idStaff")] public int? IdStaff { get; set; }
    [JsonPropertyName("staffName")] public string? StaffName { get; set; }

    /// <summary>44 = NÃO INICIADO · 45 = EM ANDAMENTO. Agrupar por aqui, não pelo texto.</summary>
    [JsonPropertyName("idStatus")] public int? IdStatus { get; set; }

    /// <summary>EM ANDAMENTO · FINALIZADO · NÃO INICIADO · DESISTÊNCIA.</summary>
    [JsonPropertyName("statusName")] public string? StatusName { get; set; }

    [JsonPropertyName("companyName")] public string? CompanyName { get; set; }
    [JsonPropertyName("description")] public string? Description { get; set; }

    // ─── datas ──────────────────────────────────────────────────────────────
    /// <summary>
    /// Início do tratamento. VEM NULO na prática — nas 12 linhas de julho de Imperatriz,
    /// nenhuma tinha valor. Foi por filtrar aqui que o card mostrava 2 de 10.
    /// </summary>
    [JsonPropertyName("dateBegin")] public DateTime? DateBegin { get; set; }

    [JsonPropertyName("dateFinish")] public DateTime? DateFinish { get; set; }

    /// <summary>Data de lançamento, em UTC. É o eixo do card e o que a franquia mostra.</summary>
    [JsonPropertyName("created")] public DateTime? Created { get; set; }

    [JsonPropertyName("modified")] public DateTime? Modified { get; set; }

    /// <summary>Chega como texto ("3680.00"), não como número — ver JsonOpts.</summary>
    [JsonPropertyName("price")] public decimal? Price { get; set; }
}

/// <summary>
/// Resposta das rotas de escrita da agenda (confirm e cancel). Elas devolvem só
/// <c>success</c> e o id — não a linha atualizada.
/// </summary>
public class SpineWriteEnvelope
{
    /// <summary>Forma da API real (v1.9.6): <c>"success"</c>.</summary>
    [JsonPropertyName("status")] public string? Status { get; set; }

    /// <summary>Forma que o guia documenta. Mantida porque estas duas rotas nunca
    /// foram exercidas — se a API responder no formato do guia, ainda lemos certo.</summary>
    [JsonPropertyName("success")] public bool? Success { get; set; }

    [JsonPropertyName("idSchedule")] public long? IdSchedule { get; set; }

    public bool Ok => Success == true
        || string.Equals(Status, "success", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Envelope dos endpoints de BI. Diferente das buscas, o BI agrupa dentro de
/// <c>data</c> em vez de devolver um array — por isso não reaproveita
/// <c>SpineSearchEnvelope</c>.
/// </summary>
public class SpineBiEnvelope<T>
{
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("success")] public bool? Success { get; set; }
    [JsonPropertyName("data")] public T? Data { get; set; }
}

/// <summary>Corpo de <c>/api/bi/leads/sources</c>.</summary>
public class SpineLeadSourcesData
{
    [JsonPropertyName("sources")] public List<SpineLeadSource>? Sources { get; set; }
    [JsonPropertyName("total")] public int Total { get; set; }
}

/// <summary>Uma origem e quantos leads vieram dela, pelo registro da franquia.</summary>
public class SpineLeadSource
{
    [JsonPropertyName("sourceName")] public string? SourceName { get; set; }
    [JsonPropertyName("total")] public int Total { get; set; }
}
