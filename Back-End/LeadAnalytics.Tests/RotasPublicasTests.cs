using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using LeadAnalytics.Api.Controllers;
using LeadAnalytics.Api.DTOs.Auth;
using LeadAnalytics.Api.Models;
using LeadAnalytics.Api.Service;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace LeadAnalytics.Tests;

/// <summary>
/// Quem entra sem login na API. O que estes testes prendem:
///  1. A API é FECHADA por padrão: a lista de rotas anônimas é exatamente a de baixo. Abrir uma
///     rota nova sem querer (esquecer o [Authorize] era o que abria) quebra o teste.
///  2. Sem token, contatos, relatório diário, relatório mensal, pagamentos, atribuições e
///     usuários respondem 401 — o 401 do JWT, com o desafio "Bearer", não um 401 do controller.
///  3. Remover unidade, apagar duplicados e administrar usuários exigem super_admin (403 para os
///     outros papéis).
///  4. Webhooks e rotas internas da X-Admin-Key continuam passando sem login.
///  5. Contatos e relatórios só respondem ao tenant dono (403 para outro tenant).
///
/// O app de teste usa os controllers DE VERDADE e a MESMA regra do Program (AuthPolicies.Configure),
/// sem banco: rota fechada responde antes de o controller existir.
/// </summary>
public class RotasPublicasTests : IAsyncLifetime
{
    private const string Emissor = "LeadAnalytics.Api";
    private const string Publico = "LeadAnalytics.Frontend";
    private static readonly string Segredo = Convert.ToBase64String(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray());

    private WebApplication _app = null!;
    private HttpClient _http = null!;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        builder.Services.AddControllers()
            .AddApplicationPart(typeof(ContactsController).Assembly)
            .AddApplicationPart(typeof(RotaNovaSemAtributoController).Assembly);
        builder.Services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(o => o.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = Emissor,
                ValidAudience = Publico,
                IssuerSigningKey = new SymmetricSecurityKey(Convert.FromBase64String(Segredo)),
                ClockSkew = TimeSpan.Zero,
            });
        builder.Services.AddAuthorization(AuthPolicies.Configure);

        _app = builder.Build();
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.MapControllers();
        await _app.StartAsync();

        _http = new HttpClient { BaseAddress = new Uri(_app.Urls.First()) };
    }

    public async Task DisposeAsync()
    {
        _http.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    // ── 1. A lista exata de rotas anônimas ─────────────────────────────────────

    /// <summary>
    /// Toda rota que responde sem login, e por quê. Mudou esta lista? Diga no PR quem chama a rota
    /// nova sem token — é exatamente a pergunta que ninguém fez nos três controllers abertos.
    /// </summary>
    private static readonly string[] RotasAnonimas =
    [
        // Login, recuperação de senha e convite: quem chama ainda não tem token.
        "POST /api/auth/login",
        "POST /api/auth/google",
        "POST /api/auth/forgot-password",
        "POST /api/auth/verify-reset-code",
        "POST /api/auth/reset-password",
        "GET /api/invitations/{token}/info",
        "POST /api/invitations/{token}/accept",

        // Webhooks: Kommo, agente (Sofia), Asaas, Meta e n8n.
        "POST /webhooks/kommo/{slug}",
        "POST /webhooks/agent/{slug}",
        "POST /webhooks/asaas/{slug}",
        "GET /api/webhooks/meta",
        "POST /api/webhooks/meta",
        "POST /api/webhooks/meta/n8n",
        "POST /api/webhooks/kommo",

        // Retorno do OAuth da Meta/Google (o navegador volta do provedor sem o nosso token).
        "GET /api/integrations/ads/{provider}/callback",

        // Vitrine pública de parceiros.
        "GET /partners/public",

        // Painel /logs: login próprio (LogsAuthService), conferido em cada ação.
        "POST /logs/auth",
        "POST /logs/logout",
        "GET /logs",
        "GET /logs/stats",
        "DELETE /logs",
        "GET /logs/stream",
        "GET /admin",

        // Rotas internas: n8n, cron do servidor e agente, com X-Admin-Key conferida na ação.
        "POST /api/config/admin-key",
        "POST /internal/alerts/overdue-installments/run",
        "GET /internal/alerts/pending-fills",
        "GET /internal/audit/kpis",
        "POST /internal/ads/account",
        "POST /internal/ads/spend",
        "GET /internal/schema",
        "POST /internal/spine/consulta-situacao/sync",
        "GET /internal/spine/tratamentos/diagnostico",
        "GET /internal/spine/reconciliacao",
        "POST /internal/spine/reconciliacao/preencher",
        "POST /internal/spine/reconciliacao/todas",
        "POST /internal/spine/datar-migracao",
        "POST /internal/spine/mover-para-tratamento",
        "POST /internal/spine/pendencias-tratamento",
        "POST /internal/spine/historico/sync",
        "GET /internal/spine/resumo",
        "POST /internal/stage-map/sync",
        "POST /internal/stage-map/corrigir-rotulos",
        "GET /internal/stage-map/saude",
        "GET /internal/sync/kommo/units",
        "POST /internal/sync/kommo/units/{unitId:int}",
        "GET /internal/sync/ads/accounts",
        "POST /internal/sync/ads/accounts/{accountId:int}",
    ];

    [Fact]
    public async Task Só_as_rotas_da_lista_abrem_sem_login()
    {
        var provedor = _app.Services.GetRequiredService<IAuthorizationPolicyProvider>();
        var anonimas = new List<string>();

        foreach (var e in ((IEndpointRouteBuilder)_app).DataSources.SelectMany(d => d.Endpoints).OfType<RouteEndpoint>())
        {
            if (!await AbreSemLoginAsync(e, provedor)) continue;
            var metodos = e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["*"];
            foreach (var m in metodos)
                anonimas.Add($"{m} /{e.RoutePattern.RawText!.TrimStart('/')}");
        }

        var abertasSemQuerer = anonimas.Except(RotasAnonimas).OrderBy(x => x).ToList();
        var listadasQueFecharam = RotasAnonimas.Except(anonimas).OrderBy(x => x).ToList();

        Assert.True(abertasSemQuerer.Count == 0,
            "Rota aberta sem login que não está na lista:\n  " + string.Join("\n  ", abertasSemQuerer));
        Assert.True(listadasQueFecharam.Count == 0,
            "Rota da lista que passou a exigir login (algum webhook/cron vai quebrar):\n  "
            + string.Join("\n  ", listadasQueFecharam));
    }

    /// <summary>A mesma decisão do AuthorizationMiddleware: [AllowAnonymous] ou política sem login.</summary>
    private static async Task<bool> AbreSemLoginAsync(Endpoint e, IAuthorizationPolicyProvider provedor)
    {
        if (e.Metadata.GetMetadata<IAllowAnonymous>() is not null) return true;
        var politica = await AuthorizationPolicy.CombineAsync(
            provedor,
            e.Metadata.GetOrderedMetadata<IAuthorizeData>(),
            e.Metadata.GetOrderedMetadata<AuthorizationPolicy>());
        return politica is null
               || !politica.Requirements.Any(r => r is DenyAnonymousAuthorizationRequirement);
    }

    [Fact]
    public void O_Program_usa_a_regra_fechada_e_mantem_health_e_docs_abertos()
    {
        // O app destes testes monta a regra pela mesma função; isto garante que o Program também.
        var program = File.ReadAllText(AcharArquivo("LeadAnalytics.Api", "Program.cs"));
        Assert.Contains("AddAuthorization(AuthPolicies.Configure)", program);

        // O healthcheck do Swarm não pode virar 401: o rolling update travaria.
        var health = program[program.IndexOf("app.MapGet(\"/health\"", StringComparison.Ordinal)..];
        Assert.Contains(".AllowAnonymous();", health[..health.IndexOf("app.MapControllers", StringComparison.Ordinal)]);

        var docs = program[program.IndexOf("app.MapScalarApiReference", StringComparison.Ordinal)..];
        Assert.Contains("}).AllowAnonymous();", docs[..docs.IndexOf("app.UseStaticFiles", StringComparison.Ordinal)]);
    }

    [Fact]
    public async Task Rota_nova_sem_atributo_nasce_fechada()
    {
        // É a regra que faltava: quem esquecer o [Authorize] numa rota nova fecha a rota,
        // em vez de abrir dados para quem tiver a URL.
        var semToken = await _http.GetAsync("/teste/rota-nova-sem-atributo");
        Assert.Equal(HttpStatusCode.Unauthorized, semToken.StatusCode);

        var comToken = await ComTokenAsync("GET", "/teste/rota-nova-sem-atributo", "unit_user", tenantId: 1);
        Assert.Equal(HttpStatusCode.OK, comToken.StatusCode);
    }

    // ── 2. Sem token: 401 do JWT ─────────────────────────────────────────────

    [Theory]
    [InlineData("GET", "/contacts?clinicId=1")]
    [InlineData("GET", "/contacts/l_1?clinicId=1")]
    [InlineData("POST", "/contacts/search?clinicId=1")]
    [InlineData("GET", "/daily-relatory/generate?tenantId=1&date=2026-10-01")]
    [InlineData("GET", "/api/relatorios/mensal-resumo?clinicId=1&mes=9&ano=2026")]
    [InlineData("GET", "/api/relatorios/mensal?clinicId=1&mes=9&ano=2026")]
    [InlineData("GET", "/payments?clinicId=1")]
    [InlineData("GET", "/assignments/attendants")]
    [InlineData("POST", "/assignments/sync")]
    [InlineData("GET", "/users")]
    [InlineData("POST", "/users")]
    [InlineData("POST", "/users/me/photo")]
    [InlineData("DELETE", "/units/1")]
    [InlineData("GET", "/contacts/admin/duplicates")]
    [InlineData("GET", "/webhooks/dashboard-overview?clinicId=1&dateFrom=2026-09-01&dateTo=2026-09-30")]
    public async Task Rota_fechada_sem_token_responde_401(string metodo, string url)
    {
        var r = await _http.SendAsync(new HttpRequestMessage(new HttpMethod(metodo), url));

        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
        // O 401 tem de ser o do JWT (desafio Bearer), não um Unauthorized() do controller.
        Assert.Contains(r.Headers.WwwAuthenticate, h => h.Scheme == "Bearer");
    }

    [Fact]
    public async Task Token_falso_tambem_responde_401()
    {
        var req = new HttpRequestMessage(HttpMethod.Get, "/contacts?clinicId=1");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "nao.e.um.jwt");
        var r = await _http.SendAsync(req);
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }

    // ── 3. Papel: só super_admin ─────────────────────────────────────────────

    [Theory]
    [InlineData("DELETE", "/units/1")]
    [InlineData("GET", "/contacts/admin/duplicates")]
    [InlineData("DELETE", "/contacts/admin/duplicates")]
    [InlineData("GET", "/users")]
    [InlineData("GET", "/users/1")]
    [InlineData("POST", "/users")]
    [InlineData("PUT", "/users/1")]
    [InlineData("DELETE", "/users/1")]
    public async Task Rota_de_super_admin_responde_403_para_outros_papeis(string metodo, string url)
    {
        foreach (var papel in new[] { "manager", "sdr", "unit_user", "analista_ti", "trafego_pago" })
        {
            var r = await ComTokenAsync(metodo, url, papel, tenantId: 1);
            Assert.True(r.StatusCode == HttpStatusCode.Forbidden, $"{papel} em {metodo} {url}: {(int)r.StatusCode}");
        }
    }

    [Theory]
    [InlineData("DELETE", "/units/1")]
    [InlineData("GET", "/contacts/admin/duplicates")]
    [InlineData("GET", "/users")]
    public async Task Super_admin_passa_pela_autorizacao(string metodo, string url)
    {
        // Sem banco no app de teste, o controller não chega a responder 200. O que importa aqui é
        // que a AUTORIZAÇÃO deixou passar: nem 401 nem 403.
        foreach (var papel in new[] { "super_admin", "super-admin" })
        {
            var r = await ComTokenAsync(metodo, url, papel, tenantId: null);
            Assert.NotEqual(HttpStatusCode.Unauthorized, r.StatusCode);
            Assert.NotEqual(HttpStatusCode.Forbidden, r.StatusCode);
        }
    }

    // ── 4. Webhooks e rotas da chave seguem sem login ───────────────────────

    [Theory]
    [InlineData("POST", "/webhooks/kommo/doutor-hernia-teste")]
    [InlineData("POST", "/webhooks/agent/doutor-hernia-teste")]
    [InlineData("POST", "/webhooks/asaas/doutor-hernia-teste")]
    [InlineData("GET", "/api/webhooks/meta?hub.mode=subscribe&hub.verify_token=x&hub.challenge=1")]
    [InlineData("POST", "/api/webhooks/meta/n8n")]
    [InlineData("POST", "/api/webhooks/kommo")]
    [InlineData("GET", "/internal/sync/kommo/units")]
    [InlineData("POST", "/internal/sync/kommo/units/14?maxLeads=500")]
    [InlineData("POST", "/internal/spine/reconciliacao/todas?de=2026-09-01&ate=2026-10-07")]
    [InlineData("POST", "/internal/spine/historico/sync?unitId=14&dias=7")]
    [InlineData("POST", "/internal/spine/consulta-situacao/sync?unitId=14&simular=false")]
    [InlineData("GET", "/internal/audit/kpis?unitId=14")]
    [InlineData("POST", "/internal/ads/spend")]
    [InlineData("POST", "/internal/alerts/overdue-installments/run")]
    [InlineData("POST", "/api/auth/login")]
    [InlineData("GET", "/api/invitations/abc/info")]
    [InlineData("POST", "/logs/auth")]
    public async Task Webhook_e_rota_da_chave_passam_sem_login(string metodo, string url)
    {
        var req = new HttpRequestMessage(new HttpMethod(metodo), url);
        if (metodo != "GET")
        {
            // Os webhooks da Kommo só aceitam formulário — é o que a Kommo manda. Com outro
            // formato a rota nem casa, e aí a regra fechada responde 401 (não é o caso real).
            req.Content = url.Contains("/kommo")
                ? new FormUrlEncodedContent(new Dictionary<string, string> { ["leads[status][0][id]"] = "1" })
                : new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
        }
        var r = await _http.SendAsync(req);

        // Sem os serviços no app de teste o controller pode até falhar (500); o que não pode é a
        // autenticação barrar a chamada antes dele.
        Assert.DoesNotContain(r.Headers.WwwAuthenticate, h => h.Scheme == "Bearer");
        Assert.NotEqual(HttpStatusCode.Forbidden, r.StatusCode);
    }

    // ── 5. Tenant: contatos e relatórios só do próprio tenant ───────────────

    [Fact]
    public async Task Contatos_de_outro_tenant_respondem_403()
    {
        var c = new ContactsController(null!, null!, Guarda(tenantId: 10), NullLogger<ContactsController>.Instance);

        Assert.IsType<ForbidResult>(await c.List(clinicId: 99));
        Assert.IsType<ForbidResult>(await c.GetById("l_1", clinicId: 99));
        Assert.IsType<ForbidResult>(await c.Delete("c_1", clinicId: 99));
        Assert.IsType<ForbidResult>(await c.FilterOptions("tags", clinicId: 99));
        Assert.IsType<ForbidResult>(await c.Search(clinicId: 99, req: null!));
    }

    [Fact]
    public async Task Relatorio_diario_de_outro_tenant_responde_403()
    {
        var c = new DailyRelatoryController(null!, Guarda(tenantId: 10));
        Assert.IsType<ForbidResult>(await c.Generate(tenantId: 99, date: new DateTime(2026, 10, 1)));
    }

    [Fact]
    public async Task Relatorio_mensal_de_outro_tenant_responde_403_e_o_dono_passa()
    {
        var servico = DispatchProxy.Create<IRelatorioService, ServicoVazio>();

        var deOutro = new RelatorioController(servico, Guarda(tenantId: 10));
        Assert.IsType<ForbidResult>(await deOutro.ObterResumoMensal(99, 9, 2026, CancellationToken.None));
        Assert.IsType<ForbidResult>(await deOutro.ObterRelatorioMensal(99, 9, 2026, CancellationToken.None));

        // O dono passa da guarda e chega ao serviço (que aqui não tem dado: 404).
        var doDono = new RelatorioController(servico, Guarda(tenantId: 10));
        Assert.IsType<NotFoundObjectResult>(await doDono.ObterResumoMensal(10, 9, 2026, CancellationToken.None));

        // Super admin passa para qualquer clínica.
        var admin = new RelatorioController(servico, Guarda(tenantId: null, papel: "super_admin"));
        Assert.IsType<NotFoundObjectResult>(await admin.ObterResumoMensal(99, 9, 2026, CancellationToken.None));
    }

    // ── Apoio ────────────────────────────────────────────────────────────────

    private static TenantUnitGuard Guarda(int? tenantId, string papel = "manager") =>
        new(null!, new UsuarioFixo(tenantId, papel), NullLogger<TenantUnitGuard>.Instance);

    private async Task<HttpResponseMessage> ComTokenAsync(string metodo, string url, string papel, int? tenantId)
    {
        var req = new HttpRequestMessage(new HttpMethod(metodo), url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token(papel, tenantId));
        if (metodo is "POST" or "PUT")
            req.Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
        return await _http.SendAsync(req);
    }

    /// <summary>Token emitido pelo MESMO serviço que emite o de produção.</summary>
    private static string Token(string papel, int? tenantId)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Secret"] = Segredo,
            ["Jwt:Issuer"] = Emissor,
            ["Jwt:Audience"] = Publico,
        }).Build();
        var jwt = new JwtTokenService(config, NullLogger<JwtTokenService>.Instance);
        var user = new User
        {
            Id = 1,
            Email = "teste@doutordigital.local",
            Name = "Teste",
            Role = papel,
            TenantId = tenantId,
        };
        return jwt.GenerateToken(user, new List<UnitSelectorOptionDto>()).token;
    }

    private static string AcharArquivo(params string[] partes)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var alvo = Path.Combine([dir.FullName, .. partes]);
            if (File.Exists(alvo)) return alvo;
            dir = dir.Parent;
        }
        throw new FileNotFoundException(string.Join("/", partes));
    }

    private sealed class UsuarioFixo(int? tenantId, string papel) : ICurrentUser
    {
        public int? UserId => 1;
        public int? TenantId => tenantId;
        public string? Role => papel;
        public string? Email => "teste@doutordigital.local";
        public bool IsSuperAdmin => Roles.IsSuperAdmin(papel);
        public bool IsAdminLevel => Roles.IsAdminLevel(papel);
        public bool IsReadOnly => Roles.IsReadOnly(papel);
        public bool IsAuthenticated => true;
        public long? SessionId => null;
        public bool IsOwner => false;
    }

    /// <summary>Serviço que responde "sem dado" a tudo: devolve o valor padrão de cada método.</summary>
    public class ServicoVazio : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? metodo, object?[]? args)
        {
            var tipo = metodo!.ReturnType;
            if (tipo == typeof(Task)) return Task.CompletedTask;
            if (tipo.IsGenericType && tipo.GetGenericTypeDefinition() == typeof(Task<>))
            {
                var t = tipo.GetGenericArguments()[0];
                object? padrao = t.IsValueType ? Activator.CreateInstance(t) : null;
                if (t == typeof(byte[])) padrao = Array.Empty<byte>();
                return typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(t).Invoke(null, [padrao]);
            }
            return tipo.IsValueType ? Activator.CreateInstance(tipo) : null;
        }
    }
}

/// <summary>Rota de teste SEM nenhum atributo de acesso — como um controller novo esquecido.</summary>
[ApiController]
[Route("teste/rota-nova-sem-atributo")]
public class RotaNovaSemAtributoController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok("dentro");
}
