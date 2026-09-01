using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.Models;
using LeadAnalytics.Api.Service;
using LeadAnalytics.Api.Service.Ads;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LeadAnalytics.Tests;

/// <summary>
/// A tela de cobertura do rastreio existe para denunciar rastreio parado. Se ELA mentir, o
/// defeito fica invisível duas vezes — e a resposta seria "mas o painel está verde".
///
/// POR QUE ESTE ARQUIVO EXISTE
/// ---------------------------
/// Dois números aqui são fáceis de errar e impossíveis de conferir a olho depois:
///
/// 1. O DENOMINADOR. A cobertura é sobre quem veio de anúncio, não sobre o total de leads.
///    Se o denominador engordar com lead orgânico, toda unidade vira vermelha e ninguém
///    olha mais a tela. Se encolher, unidade quebrada vira verde.
///
/// 2. A DIFERENÇA ENTRE ZERO E BRANCO. Unidade sem anúncio nenhum no período não tem
///    cobertura — não tem 0%. Mostrar 0% ali acusaria de quebrado quem está só sem
///    campanha rodando, que foi exatamente o risco levantado quando a tela foi pedida.
///
/// Os nomes de campo e os valores de origem daqui são os reais da Kommo, conferidos na base
/// de produção em 01/09/2026.
/// </summary>
public class CoberturaDoRastreioTests
{
    private const int Tenant = 1;
    private const int Imperatriz = 15;
    private const int Serra = 24;

    private static readonly DateTime De = new(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Ate = new(2026, 9, 1, 23, 59, 59, DateTimeKind.Utc);

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
            .UseInMemoryDatabase("cobertura-rastreio-" + nome)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics
                .InMemoryEventId.TransactionIgnoredWarning))
            .Options, new UsuarioNulo());

    private static int _proximo = 1;

    /// <summary>Um cartão da Kommo com os campos que importam — nomes reais, com os símbolos.</summary>
    private static string Cartao(string? origem, string? idAnuncio, string? extra = null)
    {
        var campos = new List<string>();
        if (origem is not null)
            campos.Add($$"""{"field_name":"⚑ Origem","value":"{{origem}}"}""");
        if (idAnuncio is not null)
            campos.Add($$"""{"field_name":"⌂ ID do anúncio","value":"{{idAnuncio}}"}""");
        if (extra is not null) campos.Add(extra);
        return "[" + string.Join(",", campos) + "]";
    }

    private static void Semear(AppDbContext db, int unitId, string unidade, string cartao,
                               DateTime? criado = null)
    {
        var id = _proximo++;
        if (db.Units.Local.All(u => u.Id != unitId))
            db.Units.Add(new Unit { Id = unitId, ClinicId = Tenant, Name = unidade });
        db.Leads.Add(new Lead
        {
            Id = id,
            ExternalId = 500_000 + id,
            Name = $"Paciente {id}",
            Phone = $"6399000{id:0000}",
            TenantId = Tenant,
            UnitId = unitId,
            CustomFieldsJson = cartao,
            CreatedAt = criado ?? new DateTime(2026, 8, 15, 12, 0, 0, DateTimeKind.Utc),
        });
    }

    private static async Task<List<LeadAnalytics.Api.DTOs.Saude.RastreioCoberturaDto>> Rodar(AppDbContext db)
    {
        db.SaveChanges();
        return await new RastreioCoberturaService(db).GetAsync(Tenant, De, Ate);
    }

    // ─── O denominador ──────────────────────────────────────────────────────────

    /// Orgânico não entra na conta. Se entrasse, esta unidade daria 50% em vez de 100%.
    [Fact]
    public async Task Lead_organico_fica_fora_do_denominador()
    {
        using var db = NovoBanco();
        Semear(db, Imperatriz, "Imperatriz", Cartao("Meta-Instagram", "120248"));
        Semear(db, Imperatriz, "Imperatriz", Cartao("Org-Instagram", null));
        Semear(db, Imperatriz, "Imperatriz", Cartao("Indicação", null));

        var r = Assert.Single(await Rodar(db));

        Assert.Equal(3, r.Leads);
        Assert.Equal(1, r.DeAnuncio);
        Assert.Equal(1, r.Rastreados);
        Assert.Equal(100, r.CoberturaPct);
        Assert.Equal("ok", r.Status);
    }

    /// "WhatsApp anúncio" é mídia paga tanto quanto Meta-*, e a base usa os dois rótulos.
    [Theory]
    [InlineData("Meta-Facebook")]
    [InlineData("Meta-Instagram")]
    [InlineData("Meta-WhatsApp")]
    [InlineData("WhatsApp anúncio")]
    public async Task Origens_pagas_entram_no_denominador(string origem)
    {
        using var db = NovoBanco(origem);
        Semear(db, Imperatriz, "Imperatriz", Cartao(origem, null));

        var r = Assert.Single(await Rodar(db));
        Assert.Equal(1, r.DeAnuncio);
    }

    /// <summary>
    /// "Canal de origem" é lista editável pela clínica e "⌂ Plataforma de origem" é escrito
    /// pelo próprio rastreio. Nenhum dos dois pode virar denominador — os dois terminam em
    /// "origem", que é a armadilha que essa regra fecha.
    /// </summary>
    [Fact]
    public async Task Campos_parecidos_com_origem_nao_contam()
    {
        using var db = NovoBanco();
        Semear(db, Imperatriz, "Imperatriz", Cartao(null, null,
            """{"field_name":"Canal de origem","value":"WhatsApp anúncio"}"""));
        Semear(db, Imperatriz, "Imperatriz", Cartao(null, null,
            """{"field_name":"⌂ Plataforma de origem","value":"facebook"}"""));

        var r = Assert.Single(await Rodar(db));

        Assert.Equal(2, r.Leads);
        Assert.Equal(0, r.DeAnuncio);
        Assert.Equal("sem_anuncio", r.Status);
    }

    // ─── Zero não é a mesma coisa que branco ────────────────────────────────────

    /// Unidade sem campanha rodando não tem cobertura. Acusá-la de 0% seria alarme falso.
    [Fact]
    public async Task Sem_lead_de_anuncio_a_cobertura_fica_em_branco()
    {
        using var db = NovoBanco();
        Semear(db, Imperatriz, "Imperatriz", Cartao("Indicação", null));
        Semear(db, Imperatriz, "Imperatriz", Cartao("Fachada", null));

        var r = Assert.Single(await Rodar(db));

        Assert.Null(r.CoberturaPct);
        Assert.Equal("sem_anuncio", r.Status);
    }

    /// Com lead de anúncio e nenhum identificado, aí sim o zero é real e tem de aparecer.
    [Fact]
    public async Task Com_anuncio_e_nada_identificado_e_sem_rastreio()
    {
        using var db = NovoBanco();
        Semear(db, Imperatriz, "Imperatriz", Cartao("Meta-Facebook", null));
        Semear(db, Imperatriz, "Imperatriz", Cartao("Meta-Instagram", null));

        var r = Assert.Single(await Rodar(db));

        Assert.Equal(0, r.CoberturaPct);
        Assert.Equal("sem_rastreio", r.Status);
    }

    // ─── As faixas ──────────────────────────────────────────────────────────────

    /// <summary>
    /// O caso que motivou a tela: a Serra respondia, rastreava um punhado e passava por
    /// normal. 1 em 10 tem de sair vermelho, não "parcial".
    /// </summary>
    [Fact]
    public async Task Cobertura_muito_baixa_e_falha_e_nao_parcial()
    {
        using var db = NovoBanco();
        Semear(db, Serra, "Serra", Cartao("Meta-Facebook", "120248"));
        for (var i = 0; i < 9; i++)
            Semear(db, Serra, "Serra", Cartao("Meta-Facebook", null));

        var r = Assert.Single(await Rodar(db));

        Assert.Equal(10, r.CoberturaPct);
        Assert.Equal("falha", r.Status);
    }

    /// 79% é o que a Imperatriz opera de verdade — o teto prático, e tem de ser verde.
    [Fact]
    public async Task O_teto_pratico_da_imperatriz_e_verde()
    {
        using var db = NovoBanco();
        for (var i = 0; i < 100; i++)
            Semear(db, Imperatriz, "Imperatriz", Cartao("Meta-Instagram", i < 79 ? "120248" : null));

        var r = Assert.Single(await Rodar(db));

        Assert.Equal(79, r.CoberturaPct);
        Assert.Equal("ok", r.Status);
    }

    // ─── Recorte e ordem ────────────────────────────────────────────────────────

    /// Lead fora da janela não pode entrar — nem no numerador, nem no denominador.
    [Fact]
    public async Task Lead_fora_do_periodo_nao_conta()
    {
        using var db = NovoBanco();
        Semear(db, Imperatriz, "Imperatriz", Cartao("Meta-Facebook", "120248"));
        Semear(db, Imperatriz, "Imperatriz", Cartao("Meta-Facebook", null),
            criado: new DateTime(2026, 5, 1, 12, 0, 0, DateTimeKind.Utc));

        var r = Assert.Single(await Rodar(db));

        Assert.Equal(1, r.Leads);
        Assert.Equal(1, r.DeAnuncio);
        Assert.Equal(100, r.CoberturaPct);
    }

    /// <summary>
    /// Quem tem mais lead de anúncio em jogo aparece primeiro: é onde o rastreio quebrado
    /// custa mais caro. Uma unidade pequena e vermelha no topo empurraria a grande para
    /// baixo da dobra.
    /// </summary>
    [Fact]
    public async Task Ordena_por_quanto_lead_de_anuncio_esta_em_jogo()
    {
        using var db = NovoBanco();
        Semear(db, Serra, "Serra", Cartao("Meta-Facebook", null));
        for (var i = 0; i < 5; i++)
            Semear(db, Imperatriz, "Imperatriz", Cartao("Meta-Instagram", "120248"));

        var r = await Rodar(db);

        Assert.Equal("Imperatriz", r[0].Unidade);
        Assert.Equal("Serra", r[1].Unidade);
    }

    /// Cartão torto não pode derrubar a página inteira — ele só não conta.
    [Fact]
    public async Task Cartao_com_json_invalido_nao_derruba()
    {
        using var db = NovoBanco();
        Semear(db, Imperatriz, "Imperatriz", "{ isto não é json válido");
        Semear(db, Imperatriz, "Imperatriz", Cartao("Meta-Facebook", "120248"));

        var r = Assert.Single(await Rodar(db));

        Assert.Equal(2, r.Leads);
        Assert.Equal(1, r.DeAnuncio);
        Assert.Equal(100, r.CoberturaPct);
    }
}
