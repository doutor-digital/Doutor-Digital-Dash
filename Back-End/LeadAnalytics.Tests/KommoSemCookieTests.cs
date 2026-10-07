using System.Net;
using LeadAnalytics.Api.Service;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LeadAnalytics.Tests;

/// <summary>
/// O "401 com token bom" do sync Kommo → painel (07/10/2026).
///
/// Toda resposta da Kommo traz <c>Set-Cookie: session_id=…; domain=.kommo.com</c>. O handler padrão
/// do HttpClient guardava esse cookie e o mandava na chamada seguinte — para OUTRA conta. A Kommo
/// dá preferência à sessão do cookie sobre o Bearer e responde 401. Na VPS, com um jarro de cookies
/// só, na ordem do cron: Balsas 200 → Imperatriz 401 → Marabá 200 → Serra 401 → Boa Vista 200 →
/// Canaã 401… Sem cookie, as mesmas contas respondem 200.
///
/// A Kommo falsa daqui faz o mesmo: devolve um session_id e recusa (401) quem volta com ele.
/// Estes testes prendem:
///  1. O KommoApiClient montado pelo DI do app (AddKommoHttpClients) nunca devolve o cookie.
///  2. O cliente nomeado "kommo" (CloudiaKommoPatchService) também não.
///  3. Controle: com o handler padrão, a Kommo falsa reproduz o 401 — o teste 1 mede algo real.
///  4. 429 espera e tenta de novo (até o máximo); 401 não é repetido.
///  5. Ritmo do sync: no máximo 5 buscas por segundo por conta.
/// </summary>
public class KommoSemCookieTests : IAsyncLifetime
{
    private WebApplication _kommo = null!;
    private string _url = null!;

    private int _chamadasConta;
    private int _chamadasContaComCookie;
    private int _chamadasCampos;
    private int _chamadasUsuarios;

    /// <summary>Quantas respostas 429 /leads/custom_fields dá antes do 200 (-1 = sempre 429).</summary>
    private int _camposResponde429Vezes;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        _kommo = builder.Build();

        // Igual à Kommo: entrega uma sessão em cookie e, se a sessão voltar, ignora o Bearer.
        _kommo.MapGet("/api/v4/account", (HttpContext ctx) =>
        {
            Interlocked.Increment(ref _chamadasConta);
            if (ctx.Request.Headers.Cookie.ToString().Contains("session_id", StringComparison.Ordinal))
            {
                Interlocked.Increment(ref _chamadasContaComCookie);
                return Results.Json(
                    new { title = "Unauthorized", status = 401, detail = "Nome de usuário ou senha inválidos" },
                    statusCode: 401);
            }
            ctx.Response.Headers.Append("Set-Cookie", "session_id=sessao-da-conta-a; path=/; HttpOnly");
            return Results.Json(new { id = 1, name = "Conta A", subdomain = "contaa" });
        });

        _kommo.MapGet("/api/v4/leads/custom_fields", (HttpContext ctx) =>
        {
            var n = Interlocked.Increment(ref _chamadasCampos);
            if (_camposResponde429Vezes < 0 || n <= _camposResponde429Vezes)
            {
                ctx.Response.Headers.RetryAfter = "0";
                return Results.StatusCode(429);
            }
            return Results.Json(new { _embedded = new { custom_fields = Array.Empty<object>() } });
        });

        _kommo.MapGet("/api/v4/users", () =>
        {
            Interlocked.Increment(ref _chamadasUsuarios);
            return Results.Json(new { title = "Unauthorized", status = 401 }, statusCode: 401);
        });

        await _kommo.StartAsync();
        _url = _kommo.Urls.First();
    }

    public async Task DisposeAsync()
    {
        await _kommo.StopAsync();
        await _kommo.DisposeAsync();
    }

    private static ServiceProvider ServicosComoNoApp()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddKommoHttpClients();
        return services.BuildServiceProvider();
    }

    // ---- 1 a 3: cookie ----

    [Fact]
    public async Task KommoApiClient_do_app_nao_leva_o_cookie_de_uma_conta_para_a_proxima()
    {
        using var sp = ServicosComoNoApp();
        var kommo = sp.GetRequiredService<KommoApiClient>();

        var contaA = await kommo.GetAccountAsync(_url, "token-da-conta-a", default);
        var contaB = await kommo.GetAccountAsync(_url, "token-da-conta-b", default);
        var contaC = await kommo.GetAccountAsync(_url, "token-da-conta-c", default);

        Assert.NotNull(contaA);
        Assert.NotNull(contaB);
        Assert.NotNull(contaC);
        Assert.Equal(3, _chamadasConta);
        Assert.Equal(0, _chamadasContaComCookie);
    }

    [Fact]
    public async Task Cliente_nomeado_kommo_tambem_nao_leva_cookie()
    {
        using var sp = ServicosComoNoApp();
        var fabrica = sp.GetRequiredService<IHttpClientFactory>();

        for (var i = 0; i < 3; i++)
        {
            var http = fabrica.CreateClient(KommoHttp.ClienteNomeado);
            using var resp = await http.GetAsync($"{_url}/api/v4/account");
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        }
        Assert.Equal(0, _chamadasContaComCookie);
    }

    [Fact]
    public async Task Controle_com_o_handler_padrao_a_segunda_conta_leva_401()
    {
        // O que rodava em produção até 07/10: HttpClient com cookies ligados (padrão).
        using var http = new HttpClient(new SocketsHttpHandler());
        var kommo = new KommoApiClient(http, NullLogger<KommoApiClient>.Instance);

        Assert.NotNull(await kommo.GetAccountAsync(_url, "token-da-conta-a", default));
        var erro = await Assert.ThrowsAsync<HttpRequestException>(
            () => kommo.GetAccountAsync(_url, "token-da-conta-b", default));

        Assert.Equal(HttpStatusCode.Unauthorized, erro.StatusCode);
        Assert.Contains("Kommo API retornou 401", erro.Message);
        Assert.Equal(1, _chamadasContaComCookie);
    }

    [Fact]
    public void Handler_da_Kommo_nao_usa_cookie()
    {
        using var handler = KommoHttp.CriarHandler();
        Assert.False(handler.UseCookies);
    }

    // ---- 4: 429 tenta de novo, 401 não ----

    [Fact]
    public async Task Resposta_429_espera_e_tenta_de_novo()
    {
        _camposResponde429Vezes = 2;
        using var sp = ServicosComoNoApp();
        var kommo = sp.GetRequiredService<KommoApiClient>();

        var campos = await kommo.GetCustomFieldsAsync(_url, "token", default);

        Assert.NotNull(campos);
        Assert.Equal(3, _chamadasCampos);
    }

    [Fact]
    public async Task Resposta_429_sem_fim_desiste_no_maximo_de_tentativas()
    {
        _camposResponde429Vezes = -1;
        using var sp = ServicosComoNoApp();
        var kommo = sp.GetRequiredService<KommoApiClient>();

        var erro = await Assert.ThrowsAsync<HttpRequestException>(
            () => kommo.GetCustomFieldsAsync(_url, "token", default));

        Assert.Equal(HttpStatusCode.TooManyRequests, erro.StatusCode);
        Assert.Equal(KommoApiClient.MaxTentativas, _chamadasCampos);
    }

    [Fact]
    public async Task Resposta_401_nao_e_repetida()
    {
        using var sp = ServicosComoNoApp();
        var kommo = sp.GetRequiredService<KommoApiClient>();

        var erro = await Assert.ThrowsAsync<HttpRequestException>(
            () => kommo.GetUsersAsync(_url, "token", 1, 50, default));

        Assert.Equal(HttpStatusCode.Unauthorized, erro.StatusCode);
        Assert.Equal(1, _chamadasUsuarios);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 4)]
    public void Sem_Retry_After_a_espera_dobra(int tentativa, int segundos)
    {
        using var resp = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        Assert.Equal(TimeSpan.FromSeconds(segundos), KommoApiClient.EsperaAntesDeTentarDeNovo(resp, tentativa));
    }

    [Fact]
    public void Retry_After_da_Kommo_vale_mas_tem_teto()
    {
        using var curto = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        curto.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(3));
        Assert.Equal(TimeSpan.FromSeconds(3), KommoApiClient.EsperaAntesDeTentarDeNovo(curto, 1));

        using var longo = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        longo.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromMinutes(5));
        Assert.Equal(KommoApiClient.EsperaMaxima, KommoApiClient.EsperaAntesDeTentarDeNovo(longo, 1));

        using var passado = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        passado.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(
            DateTimeOffset.UtcNow.AddMinutes(-1));
        Assert.Equal(TimeSpan.Zero, KommoApiClient.EsperaAntesDeTentarDeNovo(passado, 1));
    }

    // ---- 5: ritmo ----

    [Fact]
    public void Sync_faz_no_maximo_5_buscas_por_segundo_por_conta()
    {
        Assert.True(1000.0 / KommoSyncService.IntervaloEntreBuscasMs <= 5.0);
    }
}
