using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using System.Web;
using LeadAnalytics.Api.DTOs.Spine;

namespace LeadAnalytics.Api.Service.Spine;

/// <summary>
/// Raspa a situação dos tratamentos do CRM WEB da franquia (app.doutorhernia.com.br).
/// O módulo "Tratamentos" está bloqueado na API oficial (Bearer 403), então usamos o
/// front web: login por sessão (cookie PHP) → filtra a unidade → baixa o export
/// (/tratamentos/exportar, planilha HTML sem paginação) → agrega por situação e valor.
///
/// Só leitura. Sessão isolada por chamada (CookieContainer próprio); como o resultado
/// é cacheado/snapshotado, logar a cada chamada é aceitável.
/// </summary>
public partial class FranquiaWebClient(ILogger<FranquiaWebClient> logger)
{
    private const string Base = "https://app.doutorhernia.com.br";
    private const string Ua = "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0 Safari/537.36";
    private readonly ILogger<FranquiaWebClient> _logger = logger;

    /// <summary>
    /// Loga, filtra pela unidade (idCompany do CRM) num range de datas de lançamento e
    /// devolve a agregação por situação. Lança <see cref="FranquiaWebException"/> se o
    /// login falhar ou o export não vier como esperado.
    /// </summary>
    public async Task<FranquiaTratamentosDto> GetTratamentosAsync(
        string email, string password, int idCompany,
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

        // 1) Login → cookie de sessão no container.
        await http.GetAsync($"{Base}/login", ct);
        var login = await http.PostAsync($"{Base}/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["email"] = email,
            ["password"] = password,
        }), ct);
        if (!login.IsSuccessStatusCode)
            throw new FranquiaWebException($"login HTTP {(int)login.StatusCode}");

        // 2) Aplica o filtro (unidade + range de lançamento) na sessão.
        var range = $"{de:dd/MM/yyyy} - {ate:dd/MM/yyyy}";
        await http.PostAsync($"{Base}/tratamentos/filtrar", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["id_company"] = idCompany.ToString(),
            ["id_status"] = "0",
            ["created"] = range,
            ["birthdate"] = "01/01/1900 - 31/12/2100",
            ["url"] = $"{Base}/tratamentos",
        }), ct);

        // 3) Export (planilha HTML com TODOS os tratamentos do filtro).
        var exp = await http.GetAsync($"{Base}/tratamentos/exportar", ct);
        var htmlBytes = await exp.Content.ReadAsByteArrayAsync(ct);
        var html = System.Text.Encoding.UTF8.GetString(htmlBytes);
        if (!html.Contains("<td", StringComparison.OrdinalIgnoreCase) || !html.Contains("Status", StringComparison.OrdinalIgnoreCase))
            throw new FranquiaWebException("export sem tabela reconhecível (sessão expirou ou layout mudou)");

        return Parse(html);
    }

    /// <summary>Parseia o export (tabela HTML) e agrega por situação do tratamento e financeira.</summary>
    private FranquiaTratamentosDto Parse(string html)
    {
        var rows = RowRegex().Matches(html).Select(m => m.Groups[1].Value).ToList();
        if (rows.Count == 0) throw new FranquiaWebException("export sem linhas");

        static List<string> Cells(string row) => CellRegex().Matches(row)
            .Select(c => HttpUtility.HtmlDecode(StripTags().Replace(c.Groups[1].Value, " ")).Trim())
            .ToList();

        var header = Cells(rows[0]);
        int iStatus = header.FindIndex(h => h.Equals("Status", StringComparison.OrdinalIgnoreCase));
        int iPreco = header.FindIndex(h => h.Contains("Preço", StringComparison.OrdinalIgnoreCase));
        int iFin = header.FindIndex(h => h.Contains("Situação Financeira", StringComparison.OrdinalIgnoreCase));
        if (iStatus < 0 || iPreco < 0)
            throw new FranquiaWebException("colunas Status/Preço não encontradas no export");

        var porSit = new Dictionary<string, (int q, decimal v)>();
        var porFin = new Dictionary<string, int>();
        int total = 0; decimal valorTotal = 0;

        foreach (var row in rows.Skip(1))
        {
            var c = Cells(row);
            if (c.Count <= iStatus) continue;
            var situacao = string.IsNullOrWhiteSpace(c[iStatus]) ? "—" : c[iStatus];
            var valor = ParseMoeda(iPreco < c.Count ? c[iPreco] : "");
            var fin = iFin >= 0 && iFin < c.Count && !string.IsNullOrWhiteSpace(c[iFin]) ? c[iFin] : "—";

            var (q, v) = porSit.GetValueOrDefault(situacao);
            porSit[situacao] = (q + 1, v + valor);
            porFin[fin] = porFin.GetValueOrDefault(fin) + 1;
            total++; valorTotal += valor;
        }

        return new FranquiaTratamentosDto
        {
            Total = total,
            ValorTotal = valorTotal,
            AtualizadoEm = DateTime.UtcNow,
            PorSituacao = [.. porSit
                .Select(kv => new FranquiaTratamentoSituacao { Situacao = kv.Key, Quantidade = kv.Value.q, Valor = kv.Value.v })
                .OrderByDescending(x => x.Quantidade)],
            PorFinanceiro = [.. porFin
                .Select(kv => new FranquiaTratamentoSituacao { Situacao = kv.Key, Quantidade = kv.Value })
                .OrderByDescending(x => x.Quantidade)],
        };
    }

    /// <summary>"2.000,00" → 2000.00 (pt-BR: ponto = milhar, vírgula = decimal).</summary>
    private static decimal ParseMoeda(string s)
    {
        var clean = new string(s.Where(ch => char.IsDigit(ch) || ch is ',' or '.').ToArray())
            .Replace(".", "").Replace(",", ".");
        return decimal.TryParse(clean, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : 0m;
    }

    [GeneratedRegex(@"<tr[^>]*>(.*?)</tr>", RegexOptions.Singleline)]
    private static partial Regex RowRegex();
    [GeneratedRegex(@"<td[^>]*>(.*?)</td>", RegexOptions.Singleline)]
    private static partial Regex CellRegex();
    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex StripTags();
}

public class FranquiaWebException(string motivo) : Exception(motivo)
{
    public string Motivo { get; } = motivo;
}
