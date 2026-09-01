using System.Text.Json;
using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.Models;
using LeadAnalytics.Api.Service;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LeadAnalytics.Tests;

/// <summary>
/// "Zero" e "não sei" são coisas diferentes, e o painel tem de dizer qual das duas é.
///
/// POR QUE ESTE ARQUIVO EXISTE
/// ---------------------------
/// A receita passou a sair do cruzamento franquia×Kommo, que grava um vínculo por
/// tratamento. Nenhum vínculo no período significa uma de duas coisas — "não teve
/// tratamento" (R$ 0 de verdade) ou "o cruzamento nunca olhou aqui" (não sabemos) — e
/// no banco as duas são idênticas: zero linhas.
///
/// Confundi-las quebra dos dois lados. Mostrar "—" no dia 1º de cada mês, quando o mês
/// legitimamente começou zerado, faz o franqueado achar que o painel caiu. Mostrar R$ 0
/// numa unidade onde o cruzamento nunca rodou é pior: diz "não vendeu nada" para quem
/// vendeu. A marca de cobertura é o que separa os dois casos.
/// </summary>
public class CoberturaDoCruzamentoTests
{
    private const int Tenant = 1;
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
            .UseInMemoryDatabase("cobertura-" + nome)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics
                .InMemoryEventId.TransactionIgnoredWarning))
            .Options, new UsuarioNulo());

    private static void Cobertura(AppDbContext db, string de, string ate)
    {
        db.AppConfigurations.Add(new AppConfiguration
        {
            Key = $"cruzamento:cobertura:{Unidade}", Value = $"{de}|{ate}",
        });
        db.SaveChanges();
    }

    private static JsonElement Config() =>
        JsonDocument.Parse("""{"metric":"receita"}""").RootElement;

    private static Task<(double Value, int Sample, string? Note)> Receita(
        AppDbContext db, string de, string ate) =>
        new KpiConfigService(db, null!, null!, NullLogger<KpiConfigService>.Instance)
            .ComputeAsync(Tenant, Unidade, KpiSourceTypes.Franquia, Config(),
                          DateTime.Parse(de), DateTime.Parse(ate), null, "receita");

    /// Mês que começou zerado, com o cruzamento já tendo olhado: R$ 0 é a verdade.
    [Fact]
    public async Task Periodo_ja_cruzado_e_sem_tratamento_vale_zero()
    {
        using var db = NovoBanco();
        Cobertura(db, "2026-07-18", "2026-09-01");

        var r = await Receita(db, "2026-09-01", "2026-09-02");

        Assert.Equal(0d, r.Value);
        Assert.Equal("nenhum tratamento lançado no período", r.Note);
    }

    /// Unidade onde o cruzamento nunca rodou: não há número, e o card tem de dizer isso
    /// em vez de publicar um zero que significaria "não vendeu nada".
    [Fact]
    public async Task Sem_marca_de_cobertura_nao_publica_numero()
    {
        using var db = NovoBanco();

        var r = await Receita(db, "2026-09-01", "2026-09-02");

        Assert.Equal(0d, r.Value);
        Assert.Equal("cruzamento ainda não rodou para este período", r.Note);
    }

    /// Período ANTERIOR ao que o cruzamento já olhou também é desconhecido — o cron roda
    /// numa janela móvel, e janeiro não fica coberto só porque agosto foi cruzado.
    [Fact]
    public async Task Periodo_fora_da_janela_cruzada_continua_desconhecido()
    {
        using var db = NovoBanco();
        Cobertura(db, "2026-07-18", "2026-09-01");

        var r = await Receita(db, "2026-01-01", "2026-02-01");

        Assert.Equal("cruzamento ainda não rodou para este período", r.Note);
    }

    /// Período que termina depois do que já foi olhado também é desconhecido: o fim da
    /// janela é onde o conhecimento acaba.
    [Fact]
    public async Task Periodo_que_passa_do_fim_da_cobertura_e_desconhecido()
    {
        using var db = NovoBanco();
        Cobertura(db, "2026-07-18", "2026-09-01");

        var r = await Receita(db, "2026-08-25", "2026-09-30");

        Assert.Equal("cruzamento ainda não rodou para este período", r.Note);
    }

    /// Com vínculo no período, o número sai normalmente — a marca não atrapalha o caminho
    /// feliz.
    [Fact]
    public async Task Com_vinculo_a_receita_sai_normalmente()
    {
        using var db = NovoBanco();
        Cobertura(db, "2026-07-18", "2026-09-01");
        db.FranquiaLeadLinks.Add(new FranquiaLeadLink
        {
            UnitId = Unidade, IdTreatment = 1, DiaLancamento = new DateOnly(2026, 8, 14),
            Paciente = "Marlene", LeadId = 900_001, ValorKommo = 3680m,
            AtualizadoEm = DateTime.UtcNow,
        });
        db.SaveChanges();

        var r = await Receita(db, "2026-08-01", "2026-09-01");

        Assert.Equal(3680d, r.Value);
    }
}
