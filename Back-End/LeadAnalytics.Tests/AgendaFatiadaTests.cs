using System.Net;
using System.Text;
using System.Text.Json;
using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.Models;
using LeadAnalytics.Api.Options;
using LeadAnalytics.Api.Service;
using LeadAnalytics.Api.Service.Spine;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LeadAnalytics.Tests;

/// <summary>
/// A pílula "Ano" zerava Agendados, Consultas e No-show.
///
/// POR QUE ESTE ARQUIVO EXISTE
/// ---------------------------
/// A agenda da franquia só responde 100 dias por consulta. O painel pedia o ano inteiro de
/// uma vez, o cliente recusava antes de chamar ("no máximo 99 dias"), a exceção virava
/// "franquia indisponível" e o card publicava 0. Zero com cara de resultado: "a clínica não
/// agendou nada no ano".
///
/// Agora o período é fatiado em blocos de até 100 dias e somado. O risco de fatiar é contar
/// duas vezes o horário que cai na borda entre dois blocos — por isso a franquia falsa daqui
/// devolve a borda nos DOIS blocos vizinhos, de propósito, e o teste exige que conte uma vez.
/// </summary>
public class AgendaFatiadaTests
{
    private const int Unidade = 15;
    private const int Tenant = 8024;

    // ─── As fatias ──────────────────────────────────────────────────────────────

    /// O ano da pílula (366 dias) vira 4 fatias encostadas, sem buraco e sem sobreposição,
    /// cada uma dentro do limite da franquia.
    [Fact]
    public void Ano_vira_quatro_fatias_encostadas_dentro_do_limite()
    {
        var de = new DateOnly(2025, 10, 7);
        var ate = new DateOnly(2026, 10, 7);

        var blocos = SpineAvaliacoesService.BlocosDaAgenda(de, ate).ToList();

        Assert.Equal(4, blocos.Count);
        Assert.Equal(de, blocos[0].De);
        Assert.Equal(ate, blocos[^1].Ate);
        for (var i = 0; i < blocos.Count; i++)
        {
            Assert.True(blocos[i].Ate.DayNumber - blocos[i].De.DayNumber <= SpineApiClient.MaxDiasJanela);
            if (i > 0) Assert.Equal(blocos[i - 1].Ate.AddDays(1), blocos[i].De);
        }
        Assert.Equal(366, blocos.Sum(b => b.Ate.DayNumber - b.De.DayNumber + 1));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(30, 1)]
    [InlineData(100, 1)]   // 100 dias contando os dois = o limite da franquia, cabe em uma
    [InlineData(101, 2)]
    [InlineData(400, 4)]
    public void Quantidade_de_fatias(int dias, int esperado)
    {
        var de = new DateOnly(2026, 1, 1);
        Assert.Equal(esperado, SpineAvaliacoesService.BlocosDaAgenda(de, de.AddDays(dias - 1)).Count());
    }

    // ─── A soma, contra uma franquia falsa ──────────────────────────────────────

    /// Um horário de avaliação por dia, o ano inteiro, mais um horário SEM data (que a franquia
    /// devolve em toda consulta). A franquia falsa inclui o dia final pedido (a de verdade não
    /// inclui) — o pior caso para a borda.
    private sealed class FranquiaFalsa : HttpMessageHandler
    {
        public List<(DateOnly Inicio, DateOnly Fim)> Pedidos { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
        {
            using var corpo = JsonDocument.Parse(await req.Content!.ReadAsStringAsync(ct));
            var inicio = DateOnly.Parse(corpo.RootElement.GetProperty("initialDate").GetString()!);
            var fim = DateOnly.Parse(corpo.RootElement.GetProperty("endDate").GetString()!);
            Pedidos.Add((inicio, fim));

            var linhas = new List<object>();
            for (var d = inicio; d <= fim; d = d.AddDays(1))
            {
                linhas.Add(new
                {
                    idSchedule = (long)d.DayNumber,
                    // 12:00 local = 15:00Z
                    dateAttendance = d.ToDateTime(new TimeOnly(15, 0), DateTimeKind.Utc),
                    idStatus = d.Day % 2 == 0 ? SpineApiClient.ScheduleStatus.Atendido
                                              : SpineApiClient.ScheduleStatus.Desmarcado,
                    clientName = $"Paciente {d:yyyyMMdd}",
                });
            }
            linhas.Add(new { idSchedule = 1L, idStatus = SpineApiClient.ScheduleStatus.Agendado, clientName = "Sem data" });

            var json = JsonSerializer.Serialize(new
            {
                status = "success",
                data = new { data = linhas, total = linhas.Count, page = 1, rowsPerPage = 100, totalPages = 1 },
            });
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
        }
    }

    private sealed class UsuarioNulo : ICurrentUser
    {
        public int? UserId => null;
        public int? TenantId => Tenant;
        public string? Role => null;
        public string? Email => null;
        public bool IsSuperAdmin => false;
        public bool IsAdminLevel => false;
        public bool IsReadOnly => false;
        public bool IsAuthenticated => false;
        public long? SessionId => null;
        public bool IsOwner => false;
    }

    private static (SpineAvaliacoesService Servico, FranquiaFalsa Franquia, AppDbContext Db) Montar(
        [System.Runtime.CompilerServices.CallerMemberName] string nome = "")
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase("agenda-fatiada-" + nome)
            .Options, new UsuarioNulo());
        db.AppConfigurations.Add(new AppConfiguration { Key = SpineTokenStore.KeyFor(Unidade), Value = "token-de-teste" });
        db.SaveChanges();

        var franquia = new FranquiaFalsa();
        var opcoes = Microsoft.Extensions.Options.Options.Create(new SpineOptions { BaseUrl = "https://franquia.falsa" });
        var cliente = new SpineApiClient(new HttpClient(franquia), opcoes, NullLogger<SpineApiClient>.Instance);
        var tokens = new SpineTokenStore(db,
            new ProtectedTokenService(new EphemeralDataProtectionProvider()),
            NullLogger<SpineTokenStore>.Instance);
        var servico = new SpineAvaliacoesService(cliente, tokens,
            new MemoryCache(new MemoryCacheOptions()), opcoes, NullLogger<SpineAvaliacoesService>.Instance);
        return (servico, franquia, db);
    }

    /// O ano inteiro: 366 horários com data + 1 sem data = 367, cada um contado UMA vez,
    /// mesmo com a franquia devolvendo a borda nos dois blocos vizinhos.
    [Fact]
    public async Task Ano_soma_as_fatias_sem_contar_a_borda_duas_vezes()
    {
        var (servico, franquia, db) = Montar();
        using var _ = db;
        var de = new DateOnly(2025, 10, 7);
        var ate = new DateOnly(2026, 10, 7);

        var dto = await servico.GetAsync(Unidade, de, ate);

        Assert.NotNull(dto);
        Assert.Equal(367, dto!.Total);
        // Cada pedido respeita o limite da franquia (o cliente pede 1 dia a mais: até 100).
        Assert.Equal(4, franquia.Pedidos.Count);
        Assert.All(franquia.Pedidos, p => Assert.True(p.Fim.DayNumber - p.Inicio.DayNumber <= 100));
    }

    /// Acima do teto não sai pedido nenhum para a franquia.
    [Fact]
    public async Task Periodo_acima_do_teto_nao_chama_a_franquia()
    {
        var (servico, franquia, db) = Montar();
        using var _ = db;
        var de = new DateOnly(2024, 1, 1);
        var ate = de.AddDays(SpineAvaliacoesService.MaxDiasAgendaKpi); // 401 dias

        await Assert.ThrowsAsync<ArgumentException>(() => servico.GetAsync(Unidade, de, ate));
        Assert.Empty(franquia.Pedidos);
    }

    // ─── O card ─────────────────────────────────────────────────────────────────

    private static JsonElement Metrica(string m) => JsonDocument.Parse($$"""{"metric":"{{m}}"}""").RootElement;

    /// O card Agendados na pílula "Ano": número de verdade, não 0.
    [Fact]
    public async Task Card_agendados_no_ano_tem_numero()
    {
        var (servico, _, db) = Montar();
        using var __ = db;
        var kpi = new KpiConfigService(db, servico, null!, NullLogger<KpiConfigService>.Instance);

        // Como a tela manda a pílula "Ano": dia comercial, 19:00 local da véspera (22:00Z).
        var (valor, _, nota) = await kpi.MedirAsync(Tenant, Unidade, KpiSourceTypes.Franquia, Metrica("agendados"),
            new DateTime(2025, 10, 6, 22, 0, 0, DateTimeKind.Utc), new DateTime(2026, 10, 7, 22, 0, 0, DateTimeKind.Utc));

        Assert.Equal(367d, valor);
        Assert.Equal("fonte: CRM da franquia · avaliações marcadas no período", nota);
    }

    /// Período acima do teto: "—" com o motivo, nunca 0 — e sem tocar na franquia (o
    /// serviço da agenda nem existe aqui).
    [Theory]
    [InlineData("agendados")]
    [InlineData("consultas")]
    [InlineData("no_show")]
    public async Task Card_da_agenda_acima_do_teto_fica_sem_numero(string metrica)
    {
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase("agenda-teto-" + metrica).Options, new UsuarioNulo());
        var kpi = new KpiConfigService(db, null!, null!, NullLogger<KpiConfigService>.Instance);

        var (valor, _, nota) = await kpi.MedirAsync(Tenant, Unidade, KpiSourceTypes.Franquia, Metrica(metrica),
            new DateTime(2023, 10, 7, 3, 0, 0, DateTimeKind.Utc), new DateTime(2026, 10, 7, 3, 0, 0, DateTimeKind.Utc));

        Assert.Null(valor);
        Assert.Equal(KpiNotes.PeriodoLongoDemaisAgenda, nota);
    }
}
