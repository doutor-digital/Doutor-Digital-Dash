using System.Globalization;
using System.Text;
using System.Text.Json;
using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace LeadAnalytics.Api.Service.Ai;

/// <summary>
/// Tradutor de linguagem natural → filtros estruturados → query LINQ na
/// tabela <c>Leads</c>. Usa GPT-4o-mini com um prompt-sistema enxertado
/// dinamicamente com a data atual (BRT) e os domínios reais do Kommo da
/// unidade (etapas + tags conhecidas).
///
/// V1 cobre apenas os slugs que mapeiam direto pra colunas do Lead:
/// nome, email, telefone, tags, etapas, valor, data_primeiro_contato,
/// tem_telefone, ja_foi_atendido_por_chat. Demais slugs (CustomFields,
/// derivados como ja_marcou_consulta) ficam pra próximas iterações.
/// </summary>
public class LeadSearchService(
    AppDbContext db,
    OpenAiClient openAi,
    AiKeyStorage keys,
    KommoApiClient kommoApi,
    KommoStagesResolver stagesResolver,
    IMemoryCache cache,
    ILogger<LeadSearchService> logger)
{
    private static readonly TimeSpan DomainsCacheTtl = TimeSpan.FromHours(1);
    private const int DefaultLimit = 50;
    private const int MaxLimit = 200;

    /// <summary>
    /// Pega o pedido em linguagem natural, traduz pra JSON e executa.
    /// Devolve os filtros parseados + a lista de leads que casam.
    /// </summary>
    public async Task<LeadSearchResult> SearchAsync(
        int tenantId, int unitId, string naturalLanguageQuery, int limit, CancellationToken ct)
    {
        var apiKey = await keys.GetAsync(tenantId, ct);
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("Chave da OpenAI não configurada.");

        limit = Math.Clamp(limit <= 0 ? DefaultLimit : limit, 1, MaxLimit);

        var systemPrompt = await BuildSystemPromptAsync(unitId, ct);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var raw = await openAi.ChatAsync(apiKey, systemPrompt, naturalLanguageQuery, ct,
            model: OpenAiClient.DefaultModel, temperature: 0.1, maxTokens: 700);
        sw.Stop();

        ParsedFilters parsed;
        try { parsed = ParseFilters(raw); }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[lead-search] falha parseando JSON do GPT. raw={Raw}", raw);
            throw new InvalidOperationException("A IA devolveu JSON inválido. Tente reformular o pedido.");
        }

        var (leads, totalMatched, applied, ignored) = await ExecuteFiltersAsync(tenantId, unitId, parsed, limit, ct);

        return new LeadSearchResult
        {
            ParsedFilters = parsed,
            AppliedSlugs = applied,
            IgnoredSlugs = ignored,
            Leads = leads,
            TotalMatched = totalMatched,
            LimitedTo = limit,
            DurationSec = sw.Elapsed.TotalSeconds,
            Observation = parsed.Observation,
        };
    }

    // ─── Prompt building ────────────────────────────────────────────────────

    private async Task<string> BuildSystemPromptAsync(int unitId, CancellationToken ct)
    {
        var (etapas, tags) = await GetDomainsAsync(unitId, ct);

        var today = DateTime.UtcNow.AddHours(-3).ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

        var domainsBlock = new StringBuilder();
        domainsBlock.AppendLine("## DOMÍNIOS (valores reais — use estes nos filtros de seleção)");
        if (etapas.Count > 0)
            domainsBlock.AppendLine($"- **etapas**: {string.Join(", ", etapas)}");
        if (tags.Count > 0)
            domainsBlock.AppendLine($"- **tags**: {string.Join(", ", tags)}");

        // Aviso explícito sobre campos suportados em v1
        var v1Hint = "\n## CAMPOS SUPORTADOS NESTA VERSÃO (v1)\n" +
                     "Mapeiam direto pra base: nome, email, telefone, tags, etapas, valor, " +
                     "data_primeiro_contato, tem_telefone, ja_foi_atendido_por_chat.\n" +
                     "Outros slugs do prompt podem ser pedidos mas serão IGNORADOS na execução " +
                     "(reportados em 'ignorados'). Não impeça a IA de usá-los — só evite inventar.";

        return SystemPromptTemplate
            .Replace("{{DATA_ATUAL}}", today)
            + "\n\n" + domainsBlock + v1Hint;
    }

    private async Task<(List<string> Etapas, List<string> Tags)> GetDomainsAsync(int unitId, CancellationToken ct)
    {
        var cacheKey = $"lead-search-domains:{unitId}";
        if (cache.TryGetValue(cacheKey, out (List<string>, List<string>) cached)) return cached;

        var unit = await db.Units.AsNoTracking().FirstOrDefaultAsync(u => u.Id == unitId, ct);
        var etapas = new List<string>();
        var tags = new List<string>();

        // Etapas: pipelines da Kommo
        if (unit is not null && !string.IsNullOrWhiteSpace(unit.KommoSubdomain) && !string.IsNullOrWhiteSpace(unit.KommoAccessToken))
        {
            try
            {
                var pipelines = await kommoApi.GetPipelinesAsync(unit.KommoSubdomain!, unit.KommoAccessToken!, ct);
                etapas = pipelines?.Embedded?.Pipelines?
                    .SelectMany(p => p.Embedded?.Statuses ?? new())
                    .Select(s => s.Name ?? "")
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .Distinct()
                    .Take(50)
                    .ToList() ?? new();
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "[lead-search] domínios.etapas falhou (unit={Unit})", unitId);
            }
        }

        // Tags: distintas das TagsJson dos leads dessa unit
        try
        {
            var jsonSamples = await db.Leads.AsNoTracking()
                .Where(l => l.UnitId == unitId && l.TagsJson != null)
                .Select(l => l.TagsJson!)
                .Take(2000)
                .ToListAsync(ct);

            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var j in jsonSamples)
            {
                try
                {
                    using var doc = JsonDocument.Parse(j);
                    if (doc.RootElement.ValueKind != JsonValueKind.Array) continue;
                    foreach (var el in doc.RootElement.EnumerateArray())
                        if (el.ValueKind == JsonValueKind.String) set.Add(el.GetString()!);
                }
                catch { /* ignore */ }
            }
            tags = set.OrderBy(t => t).Take(60).ToList();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[lead-search] domínios.tags falhou (unit={Unit})", unitId);
        }

        var result = (etapas, tags);
        cache.Set(cacheKey, result, DomainsCacheTtl);
        return result;
    }

    // ─── JSON parsing ───────────────────────────────────────────────────────

    private static ParsedFilters ParseFilters(string raw)
    {
        // Modelo às vezes vem com ```json … ```. Strip.
        var trimmed = raw.Trim();
        if (trimmed.StartsWith("```"))
        {
            var firstNl = trimmed.IndexOf('\n');
            if (firstNl >= 0) trimmed = trimmed[(firstNl + 1)..];
            if (trimmed.EndsWith("```")) trimmed = trimmed[..^3].TrimEnd();
        }

        using var doc = JsonDocument.Parse(trimmed);
        var root = doc.RootElement;

        var op = root.TryGetProperty("operador_logico", out var oe) && oe.ValueKind == JsonValueKind.String
            ? oe.GetString() ?? "E" : "E";
        var obs = root.TryGetProperty("observacao", out var obe) && obe.ValueKind == JsonValueKind.String
            ? obe.GetString() : null;

        var filtros = new List<FilterEntry>();
        if (root.TryGetProperty("filtros", out var fe) && fe.ValueKind == JsonValueKind.Array)
        {
            foreach (var f in fe.EnumerateArray())
            {
                if (f.ValueKind != JsonValueKind.Object) continue;
                var campo = f.TryGetProperty("campo", out var ce) && ce.ValueKind == JsonValueKind.String ? ce.GetString() : null;
                var operador = f.TryGetProperty("operador", out var pe) && pe.ValueKind == JsonValueKind.String ? pe.GetString() : null;
                if (string.IsNullOrWhiteSpace(campo) || string.IsNullOrWhiteSpace(operador)) continue;
                JsonElement? value = f.TryGetProperty("valor", out var ve) ? ve.Clone() : null;
                filtros.Add(new FilterEntry { Campo = campo!, Operador = operador!, Valor = value });
            }
        }

        return new ParsedFilters
        {
            OperadorLogico = op.Equals("OU", StringComparison.OrdinalIgnoreCase) ? "OU" : "E",
            Filtros = filtros,
            Observation = obs,
        };
    }

    // ─── LINQ execution (v1: direct fields) ─────────────────────────────────

    private async Task<(List<LeadResultDto> Leads, int TotalMatched, List<string> Applied, List<string> Ignored)>
        ExecuteFiltersAsync(int tenantId, int unitId, ParsedFilters parsed, int limit, CancellationToken ct)
    {
        var q = db.Leads.AsNoTracking()
            .Where(l => l.TenantId == tenantId && l.UnitId == unitId);

        var applied = new List<string>();
        var ignored = new List<string>();
        var or = parsed.OperadorLogico == "OU";
        var nowBr = DateTime.UtcNow.AddHours(-3);

        // Para suportar OU corretamente, montamos uma lista de predicados.
        var predicates = new List<System.Linq.Expressions.Expression<Func<Lead, bool>>>();

        foreach (var f in parsed.Filtros)
        {
            var pred = TryBuildPredicate(f, nowBr, out var isApplied);
            if (isApplied && pred is not null) { applied.Add(f.Campo); predicates.Add(pred); }
            else ignored.Add(f.Campo);
        }

        if (predicates.Count > 0)
        {
            if (or)
            {
                var combined = predicates.Aggregate((a, b) => CombineOr(a, b));
                q = q.Where(combined);
            }
            else
            {
                foreach (var p in predicates) q = q.Where(p);
            }
        }

        var total = await q.CountAsync(ct);
        var leads = await q.OrderByDescending(l => l.CreatedAt)
            .Take(limit)
            .Select(l => new LeadResultDto
            {
                Id = l.Id,
                ExternalId = l.ExternalId,
                Name = l.Name,
                Phone = l.Phone,
                Email = l.Email,
                CurrentStage = l.CurrentStage,
                CurrentStageId = l.CurrentStageId,
                Source = l.Source,
                Campaign = l.Campaign,
                Price = l.Price,
                CreatedAt = l.CreatedAt,
                UpdatedAt = l.UpdatedAt,
                TagsJson = l.TagsJson,
            })
            .ToListAsync(ct);

        // Resolve stage_id → nome humano via Kommo pipelines (cacheado 1h)
        var stageMap = await stagesResolver.GetStageMapAsync(unitId, ct);
        foreach (var lead in leads)
        {
            if (lead.CurrentStageId is int sid && stageMap.TryGetValue(sid, out var stageName))
                lead.CurrentStageName = stageName;
        }

        return (leads, total, applied, ignored);
    }

    private static System.Linq.Expressions.Expression<Func<Lead, bool>> CombineOr(
        System.Linq.Expressions.Expression<Func<Lead, bool>> a,
        System.Linq.Expressions.Expression<Func<Lead, bool>> b)
    {
        var param = System.Linq.Expressions.Expression.Parameter(typeof(Lead), "l");
        var visitorA = new ReplaceParam(a.Parameters[0], param).Visit(a.Body)!;
        var visitorB = new ReplaceParam(b.Parameters[0], param).Visit(b.Body)!;
        var or = System.Linq.Expressions.Expression.OrElse(visitorA, visitorB);
        return System.Linq.Expressions.Expression.Lambda<Func<Lead, bool>>(or, param);
    }

    private sealed class ReplaceParam(System.Linq.Expressions.Expression from, System.Linq.Expressions.Expression to)
        : System.Linq.Expressions.ExpressionVisitor
    {
        public override System.Linq.Expressions.Expression? Visit(System.Linq.Expressions.Expression? node) =>
            node == from ? to : base.Visit(node);
    }

    /// <summary>
    /// Constrói um predicado LINQ pra um único filtro. Devolve null e marca
    /// <paramref name="applied"/>=false quando o slug não está mapeado em v1.
    /// </summary>
    private static System.Linq.Expressions.Expression<Func<Lead, bool>>? TryBuildPredicate(
        FilterEntry f, DateTime nowBr, out bool applied)
    {
        applied = true;
        var op = f.Operador?.ToLowerInvariant() ?? "";
        var v = f.Valor;
        var str = v?.ValueKind == JsonValueKind.String ? v.Value.GetString() : null;
        var num = v?.ValueKind == JsonValueKind.Number ? (decimal?)v.Value.GetDecimal() : null;
        var boolean = v?.ValueKind switch
        {
            JsonValueKind.True => (bool?)true,
            JsonValueKind.False => (bool?)false,
            _ => null,
        };

        switch (f.Campo)
        {
            // ─── Texto ─────────────────────────────────────────────────────
            case "nome":
                return TextOp(op, str, l => l.Name);
            case "email":
                return TextOp(op, str, l => l.Email);
            case "telefone":
                return TextOp(op, str, l => l.Phone);

            // ─── Etapas ────────────────────────────────────────────────────
            case "etapas":
                if (string.IsNullOrWhiteSpace(str)) return null;
                var st = str;
                return op switch
                {
                    "igual" => l => l.CurrentStage == st || EF.Functions.ILike(l.CurrentStage, $"%{st}%"),
                    "diferente" => l => l.CurrentStage != st,
                    _ => l => EF.Functions.ILike(l.CurrentStage, $"%{st}%"),
                };

            // ─── Tags ──────────────────────────────────────────────────────
            case "tags":
                if (v?.ValueKind == JsonValueKind.Array)
                {
                    var list = v.Value.EnumerateArray()
                        .Where(e => e.ValueKind == JsonValueKind.String)
                        .Select(e => e.GetString()!)
                        .Where(s => !string.IsNullOrWhiteSpace(s))
                        .ToList();
                    if (list.Count == 0) return null;
                    if (op == "não_contém" || op == "nao_contem")
                        return l => l.TagsJson == null || !list.Any(t => EF.Functions.ILike(l.TagsJson, $"%\"{t}\"%"));
                    if (op == "contém_todas" || op == "contem_todas")
                        return l => l.TagsJson != null && list.All(t => EF.Functions.ILike(l.TagsJson, $"%\"{t}\"%"));
                    // contém_alguma (default)
                    return l => l.TagsJson != null && list.Any(t => EF.Functions.ILike(l.TagsJson, $"%\"{t}\"%"));
                }
                if (!string.IsNullOrWhiteSpace(str))
                    return l => l.TagsJson != null && EF.Functions.ILike(l.TagsJson, $"%\"{str}\"%");
                return null;

            // ─── Valor ─────────────────────────────────────────────────────
            case "valor":
                if (num is null && op != "entre") return null;
                return op switch
                {
                    "igual" => l => l.Price == num,
                    "maior_que" => l => l.Price > num,
                    "menor_que" => l => l.Price < num,
                    "entre" when v?.ValueKind == JsonValueKind.Array =>
                        BetweenDecimal(v.Value),
                    _ => null,
                };

            // ─── Booleano: tem_telefone ────────────────────────────────────
            case "tem_telefone":
                if (boolean is null) return null;
                return boolean.Value
                    ? l => l.Phone != null && l.Phone != ""
                    : l => l.Phone == null || l.Phone == "";

            // ─── Datas ─────────────────────────────────────────────────────
            case "data_primeiro_contato":
            case "data_registro_consulta":  // alias razoável: criação no nosso DB
                return DateOp(op, v, nowBr, l => l.CreatedAt);

            // ─── Resto: NÃO suportado em v1 ────────────────────────────────
            default:
                applied = false;
                return null;
        }
    }

    private static System.Linq.Expressions.Expression<Func<Lead, bool>>? BetweenDecimal(JsonElement arr)
    {
        if (arr.GetArrayLength() != 2) return null;
        decimal a = arr[0].GetDecimal(), b = arr[1].GetDecimal();
        var (lo, hi) = a <= b ? (a, b) : (b, a);
        return l => l.Price >= lo && l.Price <= hi;
    }

    private static System.Linq.Expressions.Expression<Func<Lead, bool>>? TextOp(
        string op, string? value,
        System.Linq.Expressions.Expression<Func<Lead, string?>> selector)
    {
        var member = ((System.Linq.Expressions.MemberExpression)selector.Body).Member.Name;

        switch (op)
        {
            case "vazio":
                return member switch
                {
                    "Name" => l => l.Name == null || l.Name == "",
                    "Email" => l => l.Email == null || l.Email == "",
                    "Phone" => l => l.Phone == null || l.Phone == "",
                    _ => null,
                };
            case "preenchido":
                return member switch
                {
                    "Name" => l => l.Name != null && l.Name != "",
                    "Email" => l => l.Email != null && l.Email != "",
                    "Phone" => l => l.Phone != null && l.Phone != "",
                    _ => null,
                };
        }

        if (string.IsNullOrWhiteSpace(value)) return null;
        var v = value;
        return (op, member) switch
        {
            ("contém", "Name") or ("contem", "Name") => l => l.Name != null && EF.Functions.ILike(l.Name, $"%{v}%"),
            ("contém", "Email") or ("contem", "Email") => l => l.Email != null && EF.Functions.ILike(l.Email, $"%{v}%"),
            ("contém", "Phone") or ("contem", "Phone") => l => l.Phone != null && EF.Functions.ILike(l.Phone, $"%{v}%"),
            ("igual", "Name") => l => l.Name == v,
            ("igual", "Email") => l => l.Email == v,
            ("igual", "Phone") => l => l.Phone == v,
            ("diferente", "Name") => l => l.Name != v,
            ("diferente", "Email") => l => l.Email != v,
            ("diferente", "Phone") => l => l.Phone != v,
            ("não_contém" or "nao_contem", "Name") => l => l.Name == null || !EF.Functions.ILike(l.Name, $"%{v}%"),
            ("não_contém" or "nao_contem", "Email") => l => l.Email == null || !EF.Functions.ILike(l.Email, $"%{v}%"),
            ("não_contém" or "nao_contem", "Phone") => l => l.Phone == null || !EF.Functions.ILike(l.Phone, $"%{v}%"),
            _ => null,
        };
    }

    private static System.Linq.Expressions.Expression<Func<Lead, bool>>? DateOp(
        string op, JsonElement? v, DateTime nowBr,
        System.Linq.Expressions.Expression<Func<Lead, DateTime>> selector)
    {
        var brToUtc = TimeSpan.FromHours(3);
        DateTime BrDayStartUtc(DateTime brDate) => DateTime.SpecifyKind(brDate.Date + brToUtc, DateTimeKind.Utc);
        DateTime BrDayEndUtc(DateTime brDate) => DateTime.SpecifyKind(brDate.Date.AddDays(1).AddTicks(-1) + brToUtc, DateTimeKind.Utc);

        // selector é um simples l => l.CreatedAt — só temos esse mapeamento por enquanto
        switch (op)
        {
            case "vazio":
                return null; // Lead.CreatedAt nunca é null
            case "preenchido":
                return _ => true;

            case "hoje":
            {
                var from = BrDayStartUtc(nowBr);
                var to = BrDayEndUtc(nowBr);
                return l => l.CreatedAt >= from && l.CreatedAt <= to;
            }
            case "este_mes":
            {
                var firstDay = new DateTime(nowBr.Year, nowBr.Month, 1);
                var from = BrDayStartUtc(firstDay);
                var to = BrDayEndUtc(nowBr);
                return l => l.CreatedAt >= from && l.CreatedAt <= to;
            }
            case "nos_ultimos_dias":
                if (v?.ValueKind != JsonValueKind.Number) return null;
                var d = v.Value.GetInt32();
                var fromU = BrDayStartUtc(nowBr.AddDays(-(d - 1)));
                var toU = BrDayEndUtc(nowBr);
                return l => l.CreatedAt >= fromU && l.CreatedAt <= toU;

            case "nos_proximos_dias":
                if (v?.ValueKind != JsonValueKind.Number) return null;
                var dn = v.Value.GetInt32();
                var fromN = BrDayStartUtc(nowBr);
                var toN = BrDayEndUtc(nowBr.AddDays(dn - 1));
                return l => l.CreatedAt >= fromN && l.CreatedAt <= toN;

            case "antes":
                if (!TryParseDate(v, out var antes)) return null;
                var antesUtc = BrDayStartUtc(antes);
                return l => l.CreatedAt < antesUtc;

            case "depois":
                if (!TryParseDate(v, out var depois)) return null;
                var depoisUtc = BrDayEndUtc(depois);
                return l => l.CreatedAt > depoisUtc;

            case "entre":
                if (v?.ValueKind != JsonValueKind.Array || v.Value.GetArrayLength() != 2) return null;
                if (!TryParseDate(v.Value[0], out var ini)) return null;
                if (!TryParseDate(v.Value[1], out var fim)) return null;
                var from2 = BrDayStartUtc(ini);
                var to2 = BrDayEndUtc(fim);
                return l => l.CreatedAt >= from2 && l.CreatedAt <= to2;

            default: return null;
        }
    }

    private static bool TryParseDate(JsonElement? v, out DateTime date)
    {
        date = default;
        if (v is null) return false;
        if (v.Value.ValueKind == JsonValueKind.String)
            return DateTime.TryParse(v.Value.GetString(), CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out date);
        return false;
    }

    // ─── Tipos internos ────────────────────────────────────────────────────

    public class ParsedFilters
    {
        public string OperadorLogico { get; set; } = "E";
        public List<FilterEntry> Filtros { get; set; } = new();
        public string? Observation { get; set; }
    }

    public class FilterEntry
    {
        public string Campo { get; set; } = "";
        public string Operador { get; set; } = "";
        public JsonElement? Valor { get; set; }
    }

    public class LeadSearchResult
    {
        public ParsedFilters ParsedFilters { get; set; } = new();
        public List<string> AppliedSlugs { get; set; } = new();
        public List<string> IgnoredSlugs { get; set; } = new();
        public List<LeadResultDto> Leads { get; set; } = new();
        public int TotalMatched { get; set; }
        public int LimitedTo { get; set; }
        public double DurationSec { get; set; }
        public string? Observation { get; set; }
    }

    public class LeadResultDto
    {
        public int Id { get; set; }
        public int ExternalId { get; set; }
        public string? Name { get; set; }
        public string? Phone { get; set; }
        public string? Email { get; set; }
        public string? CurrentStage { get; set; }
        public int? CurrentStageId { get; set; }
        /// <summary>Nome humano da etapa (ex.: "Lead de entrada") resolvido via Kommo pipelines.</summary>
        public string? CurrentStageName { get; set; }
        public string? Source { get; set; }
        public string? Campaign { get; set; }
        public decimal? Price { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public string? TagsJson { get; set; }
    }

    // ─── Prompt template (do usuário, com placeholders) ────────────────────

    private const string SystemPromptTemplate = """
# PROMPT — Busca de Leads por Linguagem Natural

## Papel
Você é um motor de busca da base de leads. Recebe um pedido em linguagem natural e devolve **apenas** um objeto JSON com os filtros estruturados que respondem ao pedido. Não escreva texto fora do JSON.

## Data de referência
Hoje é **{{DATA_ATUAL}}**. Converta toda data relativa para o operador de data correspondente.

## Campos disponíveis
Texto (contém, não_contém, igual, diferente, vazio, preenchido):
nome, email, telefone, anuncio_origem_facebook, anuncio_origem_instagram,
anuncio_origem_whatsapp, anuncio_mais_recente

Número (igual, maior_que, menor_que, entre):
valor

Data (antes, depois, entre, nos_ultimos_dias, nos_proximos_dias, hoje, este_mes, vazio, preenchido):
data_nascimento, data_aniversario, data_consulta, data_registro_consulta,
data_ultima_mensagem, data_ultimo_comentario, data_ultima_consulta_cancelada,
data_ultimo_cancelamento, data_primeiro_contato, data_criacao_anuncio,
data_interacao_anuncio, data_interacao_campanha, entrou_na_etapa_em

Booleano (igual com true/false):
tem_telefone, ja_interagiu_com_anuncio, esta_em_sequencia, ja_marcou_consulta,
agendamento_manual, ja_cancelou, deixou_comentario, possui_plano_de_saude,
ja_foi_atendido_por_chat, contato_interagiu_pela_primeira_vez, bloqueado, contato_deletado

Seleção (igual, diferente, um_de, vazio):
anuncio_origem_utm, link_rastreavel, campanha, conjunto_de_anuncios,
origem_do_anuncio, local_ultima_consulta, profissional, plano_de_saude,
tipo_de_consulta, genero, canal, usuarios_atribuidos, referencias_links_fb_wpp,
status_ultima_consulta_agendada, comparecimento, conexoes, departamentos,
cidade_de_preferencia, etapas, estado_da_conversa, ultimo_motivo_conclusao_conversa

Tags (contém_alguma, contém_todas, não_contém):
tags (valor = lista de strings)

## Regras
1. Use somente os slugs acima. Se faltar campo, registre em "observacao" e NÃO invente.
2. Datas relativas → use {{DATA_ATUAL}} e o operador adequado.
3. Combine com "operador_logico" único ("E" ou "OU"). Se misturar, escolha a leitura mais provável.
4. Ambíguo → escolha a mais provável e registre em "observacao".
5. Para Seleção, prefira valores que batam com os DOMÍNIOS reais abaixo.
6. Saída = SOMENTE o JSON, sem markdown, sem comentários.

## Formato
{"operador_logico":"E","filtros":[{"campo":"<slug>","operador":"<op>","valor":<...>}],"observacao":"<texto ou null>"}
""";
}
