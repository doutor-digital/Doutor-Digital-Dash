using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using System.Web;
using LeadAnalytics.Api.DTOs.Spine;

namespace LeadAnalytics.Api.Service.Spine;

/// <summary>
/// Raspa prontuários do CRM WEB da franquia (app.doutorhernia.com.br) para auditoria.
///
/// A API oficial (app-api-prod) cobre agenda, clientes, tratamentos e BI — mas NÃO expõe
/// evolução, anamnese, CBDF nem questionário de incapacidade. Prontuário só existe no
/// front web, mesma razão pela qual <see cref="FranquiaWebClient"/> já raspa Tratamentos.
///
/// Só leitura. Sessão isolada por chamada, como o cliente irmão.
/// </summary>
public partial class FranquiaAuditoriaClient(ILogger<FranquiaAuditoriaClient> logger)
{
    private const string Base = "https://app.doutorhernia.com.br";
    private const string Ua = "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0 Safari/537.36";

    /// <summary>Pausa entre requisições — a varredura abre uma ficha de ~290 KB por tratamento.</summary>
    private const int PausaMs = 300;

    private readonly ILogger<FranquiaAuditoriaClient> _logger = logger;

    /// <summary>
    /// Loga, filtra pela unidade no range de datas e devolve um prontuário por atendimento
    /// da listagem. O agrupamento por tratamento é do serviço, não daqui.
    /// </summary>
    public async Task<List<AuditoriaProntuarioDto>> GetProntuariosAsync(
        string email, string password, int idCompany, string unidade,
        DateOnly de, DateOnly ate, CancellationToken ct = default)
    {
        using var handler = new HttpClientHandler
        {
            CookieContainer = new CookieContainer(),
            UseCookies = true,
            AllowAutoRedirect = true,
        };
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(60) };
        http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", Ua);
        // O helper de i18n do PHP deles quebra sem Accept-Language (Undefined index).
        http.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "pt-BR,pt;q=0.9");

        await http.GetAsync($"{Base}/login", ct);
        var login = await http.PostAsync($"{Base}/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["email"] = email,
            ["password"] = password,
        }), ct);
        if (!login.IsSuccessStatusCode)
            throw new FranquiaWebException($"login HTTP {(int)login.StatusCode}");

        // O filtro fica na SESSÃO do servidor, não na URL: aplicar antes de paginar.
        //
        // `created` não pode ir vazio. Com string vazia o controller descarta o filtro
        // inteiro e devolve todas as unidades — falha silenciosa, a listagem parece funcionar.
        var range = $"{de:dd/MM/yyyy} - {ate:dd/MM/yyyy}";
        await http.PostAsync($"{Base}/atendimentos/filtrar", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["id_company"] = idCompany.ToString(),
            ["id_staff"] = "0",
            ["created"] = range,
            ["keyword_attendance"] = "",
        }), ct);

        var lista = new List<AuditoriaAtendimentoDto>();
        var primeira = await http.GetStringAsync($"{Base}/atendimentos/listagem/0", ct);
        var total = TotalRegistros(primeira);
        lista.AddRange(ParseListagem(primeira));

        var porPagina = Math.Max(lista.Count, 1);
        for (var offset = porPagina; offset < total; offset += porPagina)
        {
            await Task.Delay(PausaMs, ct);
            lista.AddRange(ParseListagem(await http.GetStringAsync($"{Base}/atendimentos/listagem/{offset}", ct)));
        }

        // Se o filtro falhar a listagem vem global; conferir a unidade evita varrer a rede inteira.
        var alvo = lista.Where(a => a.Unidade.Trim().Equals(unidade.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
        if (alvo.Count != lista.Count)
            _logger.LogWarning("Auditoria: {Fora} linha(s) de outra unidade descartadas (unit CRM {Company})",
                lista.Count - alvo.Count, idCompany);

        var prontuarios = new List<AuditoriaProntuarioDto>();
        foreach (var at in alvo)
        {
            await Task.Delay(PausaMs, ct);
            try
            {
                var html = await http.GetStringAsync($"{Base}/atendimentos/acompanhar/{at.Id}", ct);
                prontuarios.Add(ParseProntuario(html, at));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Auditoria: falha ao abrir a ficha do atendimento {Id}", at.Id);
            }
        }

        return prontuarios;
    }

    private static int TotalRegistros(string html)
    {
        var m = TotalRegex().Match(html);
        return m.Success ? int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
    }

    private static List<string> Cells(string row) => [.. CellRegex().Matches(row)
        .Select(c => HttpUtility.HtmlDecode(StripTags().Replace(c.Groups[1].Value, " ")).Trim())];

    private static List<AuditoriaAtendimentoDto> ParseListagem(string html)
    {
        var saida = new List<AuditoriaAtendimentoDto>();

        foreach (Match row in RowRegex().Matches(html))
        {
            var c = Cells(row.Groups[1].Value);
            if (c.Count < 9) continue;

            var idTxt = c[0].TrimStart('#').Trim();
            if (!long.TryParse(idTxt, NumberStyles.Any, CultureInfo.InvariantCulture, out var id)) continue;

            var dur = DigitosRegex().Match(c[4]);
            int? duracao = dur.Success ? int.Parse(dur.Groups[1].Value, CultureInfo.InvariantCulture) : null;

            // A listagem calcula duração sobre epoch para atendimento em aberto e devolve
            // ~29.774.500 minutos (≈56 anos). Acima de um dia o número não é informação.
            if (duracao is > 1440) duracao = null;

            saida.Add(new AuditoriaAtendimentoDto
            {
                Id = id,
                Paciente = c[1],
                Inicio = string.IsNullOrWhiteSpace(c[2]) ? null : c[2],
                Termino = string.IsNullOrWhiteSpace(c[3]) ? null : c[3],
                DuracaoMin = duracao,
                Fisioterapeuta = c[5],
                Unidade = c[6],
                Situacao = c[8],
            });
        }

        return saida;
    }

    /// <summary>"11/08/2026" ou "11/08/26" → DateOnly.</summary>
    private static DateOnly? ParseData(string? br)
    {
        if (string.IsNullOrWhiteSpace(br)) return null;

        var m = DataRegex().Match(br);
        if (!m.Success) return null;

        var ano = m.Groups[3].Value.Length == 2 ? $"20{m.Groups[3].Value}" : m.Groups[3].Value;
        return DateOnly.TryParseExact($"{m.Groups[1].Value}/{m.Groups[2].Value}/{ano}", "dd/MM/yyyy",
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
    }

    /// <summary>HTML do editor → texto, preservando as quebras de parágrafo.</summary>
    private static string HtmlParaTexto(string html)
    {
        var t = BrRegex().Replace(html, "\n");
        t = FimBlocoRegex().Replace(t, "\n");
        t = HttpUtility.HtmlDecode(StripTags().Replace(t, ""));
        t = EspacosRegex().Replace(t, " ");
        t = LinhasRegex().Replace(t, "\n\n");
        return t.Trim();
    }

    private AuditoriaProntuarioDto ParseProntuario(string html, AuditoriaAtendimentoDto at)
    {
        var texto = HttpUtility.HtmlDecode(StripTags().Replace(html, " "));
        texto = EspacosRegex().Replace(texto, " ");

        // Avaliação avulsa ("Iniciando Avaliação") não tem aba de evolução nem tratamento.
        // Sem essa distinção toda avaliação seria acusada de sessão sem evolução registrada.
        var tipo = html.Contains("id=\"evolution-tab\"", StringComparison.OrdinalIgnoreCase) ? "tratamento" : "avaliacao";

        var idTreatment = Oculto(html, "id_treatment");
        var mRealiz = RealizadosRegex().Match(texto);
        var mPrimeira = PrimeiraRegex().Match(texto);
        var mIdade = IdadeRegex().Match(texto);
        var mEste = EsteRegex().Match(texto);
        var mNome = NomeRegex().Match(html);

        var planos = BadgeRegex().Matches(html)
            .Select(m => HttpUtility.HtmlDecode(StripTags().Replace(m.Groups[1].Value, " ")).Trim())
            .Where(s => s.Length > 0)
            .ToList();

        var cbdf = SelecionadoRegex().Matches(TrechoAba(html, "CBDF"))
            .Select(m => HttpUtility.HtmlDecode(StripTags().Replace(m.Groups[1].Value, " ")).Trim())
            .Where(s => s.Length > 0 && !s.StartsWith("N/A", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var prognostico = SelecionadoRegex().Matches(TrechoAba(html, "prognosis"))
            .Select(m => HttpUtility.HtmlDecode(StripTags().Replace(m.Groups[1].Value, " ")).Trim())
            .FirstOrDefault(s => s.Length > 0);

        return new AuditoriaProntuarioDto
        {
            Chave = idTreatment is not null ? $"t{idTreatment}" : $"a{at.Id}",
            Tipo = tipo,
            IdClient = Oculto(html, "id_client"),
            IdTreatment = idTreatment,
            NomePaciente = mNome.Success
                ? HttpUtility.HtmlDecode(StripTags().Replace(mNome.Groups[1].Value, " ")).Trim()
                : at.Paciente,
            Idade = mIdade.Success ? int.Parse(mIdade.Groups[1].Value, CultureInfo.InvariantCulture) : null,
            Plano = string.Join(" · ", planos),
            PrimeiraConsulta = mPrimeira.Success ? mPrimeira.Groups[1].Value : null,
            PrimeiraIso = mPrimeira.Success ? ParseData(mPrimeira.Groups[1].Value) : null,
            Realizados = mRealiz.Success ? int.Parse(mRealiz.Groups[1].Value, CultureInfo.InvariantCulture) : null,
            Previstos = mRealiz.Success ? int.Parse(mRealiz.Groups[2].Value, CultureInfo.InvariantCulture) : null,
            EsteAtendimento = mEste.Success ? int.Parse(mEste.Groups[1].Value, CultureInfo.InvariantCulture) : null,
            Cbdf = cbdf,
            Prognostico = prognostico,
            Principal = at,
            Atendimentos = [at],
            Evolucoes = ParseEvolucoes(html),
            Questionario = ParseQuestionario(html),
        };
    }

    /// <summary>Recorta o bloco de uma aba pelo id, para não pegar &lt;option selected&gt; de outra.</summary>
    private static string TrechoAba(string html, string id)
    {
        var i = html.IndexOf($"id=\"{id}\"", StringComparison.OrdinalIgnoreCase);
        if (i < 0) return "";

        var fim = html.IndexOf("</form>", i, StringComparison.OrdinalIgnoreCase);
        return fim > i ? html[i..fim] : html[i..];
    }

    private static long? Oculto(string html, string nome)
    {
        var m = Regex.Match(html, $@"name=""{Regex.Escape(nome)}""[^>]*value=""(\d+)""", RegexOptions.IgnoreCase);
        return m.Success ? long.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : null;
    }

    private static List<AuditoriaEvolucaoDto> ParseEvolucoes(string html)
    {
        var saida = new List<AuditoriaEvolucaoDto>();
        var bloco = TrechoDiv(html, "evolution");

        foreach (Match li in TimelineRegex().Matches(bloco))
        {
            var corpo = li.Groups[1].Value;

            var mData = PrimeiroStrongRegex().Match(corpo);
            var data = mData.Success ? HttpUtility.HtmlDecode(StripTags().Replace(mData.Groups[1].Value, " ")).Trim() : "";

            var mProf = FloatEndRegex().Match(corpo);
            var prof = mProf.Success
                ? EspacosRegex().Replace(HttpUtility.HtmlDecode(StripTags().Replace(mProf.Groups[1].Value, " ")), " ").Trim()
                : "";

            // O corpo NÃO sai de um <p>: o editor grava <P> aninhado dentro do <p> do
            // template, e todo parser HTML fecha o parágrafo externo ao achar o interno —
            // o <p> externo chega vazio. Tira-se a data e o profissional do <li> inteiro.
            var resto = PrimeiroStrongRegex().Replace(corpo, "", 1);
            resto = FloatEndRegex().Replace(resto, "", 1);
            var texto = HtmlParaTexto(resto);

            if (data.Length == 0 && texto.Length == 0) continue;

            var (rotulo, corpoDia) = ExtrairDias(texto);
            var (evaIni, evaFim) = ExtrairEva(texto);
            var linhas = texto.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();

            saida.Add(new AuditoriaEvolucaoDto
            {
                Data = data,
                DataIso = ParseData(data),
                Profissional = prof,
                Protocolo = linhas.FirstOrDefault(l => ProtocoloRegex().IsMatch(l) && !DoDiaRegex().IsMatch(l)) ?? "",
                DiaRotulo = rotulo,
                DiaCorpo = corpoDia,
                EvaInicial = evaIni,
                EvaFinal = evaFim,
                Texto = texto,
            });
        }

        // A timeline vem do mais recente para o mais antigo; a auditoria lê cronológico.
        saida.Reverse();
        return saida;
    }

    private static string TrechoDiv(string html, string id)
    {
        var i = html.IndexOf($"id=\"{id}\"", StringComparison.OrdinalIgnoreCase);
        if (i < 0) return "";

        var fim = html.IndexOf("<div class=\"tab-pane", i + 10, StringComparison.OrdinalIgnoreCase);
        return fim > i ? html[i..fim] : html[i..];
    }

    private static (int? Rotulo, int? Corpo) ExtrairDias(string texto)
    {
        int? rotulo = null;
        foreach (var linha in texto.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).Take(4))
        {
            var m = DiaSozinhoRegex().Match(linha);
            if (!m.Success) continue;
            rotulo = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            break;
        }

        var mc = DoDiaRegex().Match(texto);
        int? corpo = mc.Success ? int.Parse(mc.Groups[1].Value, CultureInfo.InvariantCulture) : null;
        return (rotulo, corpo);
    }

    /// <summary>
    /// EVA de abertura e de fecho. O fecho é o PRIMEIRO marcador de encerramento, não o
    /// último: registros com "APÓS AJUSTES, EVA 0" seguidos de "AO TÉRMINO, SEM QUEIXAS"
    /// perderiam o EVA final se a busca partisse do marcador mais tardio.
    /// </summary>
    private static (int? Inicial, int? Final) ExtrairEva(string texto)
    {
        var t = texto.ToUpperInvariant();
        var todos = EvaRegex().Matches(t)
            .Select(m => (Valor: int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), Pos: m.Index))
            .Where(x => x.Valor is >= 0 and <= 10)
            .ToList();
        if (todos.Count == 0) return (null, null);

        var marcas = new[] { "TERMINA", "FINALIZA", "AO TÉRMINO", "APÓS AJUSTES", "APÓS FLEXO" }
            .Select(m => t.IndexOf(m, StringComparison.Ordinal))
            .Where(i => i >= 0)
            .ToList();

        var iFecho = marcas.Count > 0 ? marcas.Min() : -1;
        var abre = iFecho >= 0 ? todos.Where(x => x.Pos < iFecho).ToList() : todos;
        var fecho = iFecho >= 0 ? todos.Where(x => x.Pos > iFecho).ToList() : [];

        return (abre.Count > 0 ? abre[0].Valor : null, fecho.Count > 0 ? fecho[^1].Valor : null);
    }

    private static AuditoriaQuestionarioDto? ParseQuestionario(string html)
    {
        var bloco = TrechoDiv(html, "incapacidade");
        if (bloco.Length == 0) return null;

        var marcados = QuestaoRegex().Matches(bloco);
        if (marcados.Count == 0) return null;

        int ini = 0, fim = 0, respIni = 0, respFim = 0;
        foreach (Match m in marcados)
        {
            var ehFinal = m.Groups[1].Value.Length > 0;
            var sim = m.Groups[3].Value.Equals("S", StringComparison.OrdinalIgnoreCase);
            if (ehFinal)
            {
                respFim++;
                if (sim) fim++;
            }
            else
            {
                respIni++;
                if (sim) ini++;
            }
        }

        var mCriado = CriadoRegex().Match(HttpUtility.HtmlDecode(StripTags().Replace(bloco, " ")));
        var criado = mCriado.Success ? EspacosRegex().Replace(mCriado.Groups[1].Value, " ").Trim() : null;

        return new AuditoriaQuestionarioDto
        {
            CriadoEm = criado,
            CriadoEmIso = ParseData(criado),
            EscoreInicial = respIni > 0 ? ini : null,
            EscoreFinal = respFim > 0 ? fim : null,
        };
    }

    [GeneratedRegex(@"<tr[^>]*>(.*?)</tr>", RegexOptions.Singleline)]
    private static partial Regex RowRegex();
    [GeneratedRegex(@"<td[^>]*>(.*?)</td>", RegexOptions.Singleline)]
    private static partial Regex CellRegex();
    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex StripTags();
    [GeneratedRegex(@"Total de\s*(\d+)\s*registros", RegexOptions.IgnoreCase)]
    private static partial Regex TotalRegex();
    [GeneratedRegex(@"(\d+)")]
    private static partial Regex DigitosRegex();
    [GeneratedRegex(@"(\d{2})/(\d{2})/(\d{2,4})")]
    private static partial Regex DataRegex();
    [GeneratedRegex(@"<li class=""timeline-item"">(.*?)</li>", RegexOptions.Singleline)]
    private static partial Regex TimelineRegex();
    [GeneratedRegex(@"<strong>(.*?)</strong>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex PrimeiroStrongRegex();
    [GeneratedRegex(@"<span[^>]*float-end[^>]*>(.*?)</span>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex FloatEndRegex();
    [GeneratedRegex(@"<\s*br\s*/?\s*>", RegexOptions.IgnoreCase)]
    private static partial Regex BrRegex();
    [GeneratedRegex(@"</\s*(p|div|li|h\d)\s*>", RegexOptions.IgnoreCase)]
    private static partial Regex FimBlocoRegex();
    [GeneratedRegex(@"[ \t]+")]
    private static partial Regex EspacosRegex();
    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex LinhasRegex();
    [GeneratedRegex(@"^DIA\s+(\d+)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex DiaSozinhoRegex();
    [GeneratedRegex(@"PROTOCOLO\s+(?:B\s+)?(?:DO\s+|DESTE\s+)?DIA\s+(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex DoDiaRegex();
    [GeneratedRegex(@"^PROTOCOLO\s+\w", RegexOptions.IgnoreCase)]
    private static partial Regex ProtocoloRegex();
    [GeneratedRegex(@"EVA\s*:?\s*(\d{1,2})")]
    private static partial Regex EvaRegex();
    [GeneratedRegex(@"name=""(f_)?question_(\d+)""[^>]*value=""([SN])""[^>]*\schecked", RegexOptions.IgnoreCase)]
    private static partial Regex QuestaoRegex();
    [GeneratedRegex(@"Criado em:\s*([\d/]+\s*[\d:]*)", RegexOptions.IgnoreCase)]
    private static partial Regex CriadoRegex();
    [GeneratedRegex(@"Atendimentos realizados:\s*(\d+)\s*de\s*(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex RealizadosRegex();
    [GeneratedRegex(@"Primeira consulta em:\s*([\d/]+)", RegexOptions.IgnoreCase)]
    private static partial Regex PrimeiraRegex();
    [GeneratedRegex(@"Idade:\s*(\d+)\s*anos", RegexOptions.IgnoreCase)]
    private static partial Regex IdadeRegex();
    [GeneratedRegex(@"Este atendimento:\s*(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex EsteRegex();
    [GeneratedRegex(@"<h3>\s*<a[^>]*>(.*?)</a>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex NomeRegex();
    [GeneratedRegex(@"<span class=""badge rounded-pill bg-primary[^""]*"">(.*?)</span>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex BadgeRegex();
    [GeneratedRegex(@"<option[^>]*\sselected[^>]*>(.*?)</option>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex SelecionadoRegex();
}
