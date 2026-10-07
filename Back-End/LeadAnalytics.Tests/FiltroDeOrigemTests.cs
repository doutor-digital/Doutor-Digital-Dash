using System.Text.Json;
using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.Models;
using LeadAnalytics.Api.Service;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LeadAnalytics.Tests;

/// <summary>
/// Filtros › Origem do dashboard.
///
/// POR QUE ESTE ARQUIVO EXISTE
/// ---------------------------
/// A lista do filtro oferecia o ⚑ Origem do cartão (Meta-Instagram, Indicação…), mas a conta
/// filtrava a coluna leads.Source — "Kommo" em 100% dos leads. Escolher "Meta-Instagram"
/// zerava os gráficos de baixo e deixava os cards de cima iguais. Os testes daqui travam as
/// duas pontas: o filtro casa com o campo do cartão, e o card de Leads obedece ao filtro.
/// </summary>
public class FiltroDeOrigemTests
{
    private const int Tenant = 8024;
    private const int Unidade = 15;

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

    private static AppDbContext NovoBanco([System.Runtime.CompilerServices.CallerMemberName] string nome = "") =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase("origem-" + nome)
            .Options, new UsuarioNulo());

    private static readonly DateTime Criado = new(2026, 9, 15, 15, 0, 0, DateTimeKind.Utc);

    private static Lead NovoLead(int id, string? campo, string? origem) => new()
    {
        Id = id,
        Name = $"Paciente {id}",
        Phone = $"6399000{id:0000}",
        TenantId = Tenant,
        UnitId = Unidade,
        CreatedAt = Criado,
        UpdatedAt = Criado,
        Status = "active",
        // A coluna que o filtro antigo lia: "Kommo" em todo lead, como em produção.
        Source = "Kommo",
        CustomFieldsJson = campo is null ? null
            : $$"""[{"field_name":"{{campo}}","value":"{{origem}}"},{"field_name":"⌂ Plataforma de origem","value":"Meta-Instagram"}]""",
    };

    private static void Semear(AppDbContext db)
    {
        db.Leads.AddRange(
            NovoLead(1, "⚑ Origem", "Meta-Instagram"),
            NovoLead(2, "⚑ Origem", "Meta-Instagram"),
            NovoLead(3, "Origem", "Meta-Instagram"),        // base antiga, sem o símbolo
            NovoLead(4, "⚑ Origem", "Indicação"),
            NovoLead(5, "⚑ Origem", "Meta-Facebook"),
            NovoLead(6, null, null));                         // cartão sem campo nenhum
        db.SaveChanges();
    }

    [Fact]
    public async Task Filtra_pelo_campo_do_cartao_e_nao_pela_coluna_Source()
    {
        using var db = NovoBanco();
        Semear(db);

        var q = await FiltroDeOrigem.AplicarAsync(db, db.Leads.AsQueryable(), Tenant, Unidade, "Meta-Instagram");

        Assert.Equal(new[] { 1, 2, 3 }, q.Select(l => l.Id).OrderBy(x => x).ToArray());
    }

    /// "Kommo" era a única opção que funcionava no filtro antigo. Não é origem: não casa nada.
    [Fact]
    public async Task Kommo_nao_e_origem()
    {
        using var db = NovoBanco();
        Semear(db);

        var q = await FiltroDeOrigem.AplicarAsync(db, db.Leads.AsQueryable(), Tenant, Unidade, "Kommo");

        Assert.Empty(q.ToList());
    }

    /// "⌂ Plataforma de origem" termina em "origem" e não é o campo — não pode casar.
    [Fact]
    public async Task Campo_parecido_nao_conta()
    {
        using var db = NovoBanco();
        db.Leads.Add(NovoLead(9, "⌂ Plataforma de origem", "Indicação"));
        db.SaveChanges();

        var q = await FiltroDeOrigem.AplicarAsync(db, db.Leads.AsQueryable(), Tenant, Unidade, "Indicação");

        Assert.Empty(q.ToList());
    }

    [Fact]
    public async Task Origem_vazia_nao_filtra()
    {
        using var db = NovoBanco();
        Semear(db);

        var q = await FiltroDeOrigem.AplicarAsync(db, db.Leads.AsQueryable(), Tenant, Unidade, "  ");

        Assert.Equal(6, q.Count());
    }

    /// O card Leads (fonte "created") obedece ao filtro, como já obedecia ao de usuário.
    [Fact]
    public async Task Card_de_leads_obedece_ao_filtro()
    {
        using var db = NovoBanco();
        Semear(db);
        var kpi = new KpiConfigService(db, null!, null!, NullLogger<KpiConfigService>.Instance);
        var vazio = JsonDocument.Parse("{}").RootElement;
        var de = new DateTime(2026, 9, 1, 3, 0, 0, DateTimeKind.Utc);
        var ate = new DateTime(2026, 10, 1, 3, 0, 0, DateTimeKind.Utc);

        var (todos, _, _) = await kpi.MedirAsync(Tenant, Unidade, KpiSourceTypes.CreatedInPeriod, vazio, de, ate);
        var (instagram, _, _) = await kpi.MedirAsync(Tenant, Unidade, KpiSourceTypes.CreatedInPeriod, vazio, de, ate,
            origem: "Meta-Instagram");

        Assert.Equal(6d, todos);
        Assert.Equal(3d, instagram);
    }

    /// No Postgres o filtro é uma subconsulta no banco, com a MESMA regra da lista de opções
    /// (GetDistinctSourcesAsync). Confere o SQL sem precisar de banco: se alguém trocar a
    /// regra de um lado só, a opção da lista volta a não casar com lead nenhum.
    [Fact]
    public async Task No_Postgres_vira_subconsulta_com_a_regra_da_lista()
    {
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=nao_conecta").Options, new UsuarioNulo());

        var q = await FiltroDeOrigem.AplicarAsync(
            db, db.Leads.Where(l => l.TenantId == Tenant), Tenant, Unidade, " Meta-Instagram ");
        var sql = q.ToQueryString();

        Assert.Contains("btrim(lower(e.value->>'field_name'), '⚑⌂☎ ') = 'origem'", sql);
        Assert.Contains("btrim(e.value->>'value') = @p0", sql);
        Assert.Contains("jsonb_typeof", sql);
        Assert.Contains("'Meta-Instagram'", sql);          // valor já sem os espaços das pontas
        Assert.DoesNotContain("\"Source\" =", sql);        // a coluna antiga não filtra nada
    }
}
