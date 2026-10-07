using System.Text.Json;
using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.DTOs.Response;
using LeadAnalytics.Api.Models;
using LeadAnalytics.Api.Service;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LeadAnalytics.Tests;

/// <summary>
/// "—" não é zero, e o painel tem de saber a diferença até o fim do caminho.
///
/// POR QUE ESTE ARQUIVO EXISTE
/// ---------------------------
/// O serviço já devolvia "não sei" em vários casos (cruzamento que nunca rodou, franquia fora),
/// mas como um 0 com uma nota em texto livre. O controller só reconhecia duas notas; todas as
/// outras viravam número: R$ 0 de receita num período nunca cruzado, Agendados = 0 na pílula
/// "Ano". Aqui o valor nulo é que manda — e o card recebe o motivo em português.
/// </summary>
public class KpiSemNumeroTests
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
            .UseInMemoryDatabase("sem-numero-" + nome)
            .Options, new UsuarioNulo());

    private static Task<(double? Value, int Sample, string? Note)> Receita(AppDbContext db, string de, string ate) =>
        new KpiConfigService(db, null!, null!, NullLogger<KpiConfigService>.Instance)
            .MedirAsync(Tenant, Unidade, KpiSourceTypes.Franquia,
                JsonDocument.Parse("""{"metric":"receita"}""").RootElement,
                DateTime.Parse(de), DateTime.Parse(ate), null, "receita");

    private static void Cobertura(AppDbContext db, string de, string ate)
    {
        db.AppConfigurations.Add(new AppConfiguration { Key = $"cruzamento:cobertura:{Unidade}", Value = $"{de}|{ate}" });
        db.SaveChanges();
    }

    private static void Vinculo(AppDbContext db, int id, string dia, decimal? preco, decimal? valorKommo)
    {
        db.FranquiaLeadLinks.Add(new FranquiaLeadLink
        {
            UnitId = Unidade, IdTreatment = id, DiaLancamento = DateOnly.Parse(dia),
            Paciente = $"Paciente {id}", LeadId = 900_000 + id,
            PrecoFranquia = preco, ValorKommo = valorKommo, AtualizadoEm = DateTime.UtcNow,
        });
        db.SaveChanges();
    }

    // ─── Receita ────────────────────────────────────────────────────────────────

    /// O defeito do mapa (07/10): período que o cruzamento nunca olhou devolvia 0.
    [Fact]
    public async Task Receita_de_periodo_nunca_cruzado_nao_tem_numero()
    {
        using var db = NovoBanco();
        Cobertura(db, "2026-01-01", "2026-10-07");

        var r = await Receita(db, "2025-06-01", "2025-07-01");

        Assert.Null(r.Value);
        Assert.Equal(KpiNotes.CruzamentoNaoRodou, r.Note);
    }

    /// Período já cruzado e sem tratamento: aí sim é R$ 0.
    [Fact]
    public async Task Receita_de_periodo_cruzado_e_vazio_vale_zero()
    {
        using var db = NovoBanco();
        Cobertura(db, "2026-01-01", "2026-10-07");

        var r = await Receita(db, "2026-09-01", "2026-09-02");

        Assert.Equal(0d, r.Value);
    }

    /// A pílula "Ano" começa em out/2025, antes do cruzamento: o número sai, mas a nota diz
    /// de quando ele vale — ninguém lê R$ X como o ano inteiro.
    [Fact]
    public async Task Receita_que_comeca_antes_do_cruzamento_avisa_o_corte()
    {
        using var db = NovoBanco();
        Cobertura(db, "2026-01-01", "2026-10-07");
        Vinculo(db, 1, "2026-03-10", 3680m, null);

        var r = await Receita(db, "2025-10-07", "2026-10-07");

        Assert.Equal(3680d, r.Value);
        Assert.Equal("1 tratamento · todos com valor · cruzamento só a partir de 01/01/2026", r.Note);
    }

    /// Dentro da cobertura, a nota fica como já era.
    [Fact]
    public async Task Receita_dentro_do_cruzamento_nao_ganha_aviso()
    {
        using var db = NovoBanco();
        Cobertura(db, "2026-01-01", "2026-10-07");
        Vinculo(db, 1, "2026-09-10", null, 2500m);

        var r = await Receita(db, "2026-09-01", "2026-10-01");

        Assert.Equal(2500d, r.Value);
        Assert.Equal("1 tratamento · todos com valor", r.Note);
    }

    // ─── Publicação no dashboard ────────────────────────────────────────────────

    [Fact]
    public void Sem_numero_nao_vira_zero_e_leva_o_motivo()
    {
        var dto = new DashboardOverviewDto();

        PublicacaoDeKpi.Publicar(dto, "receita", null, KpiNotes.CruzamentoNaoRodou);

        Assert.False(dto.KpiOverrides.ContainsKey("receita"));
        Assert.Equal("O cruzamento ainda não rodou para este período (roda todo dia às 05:40).",
            dto.KpisSemNumero["receita"]);
    }

    /// A falha que zerava a pílula "Ano" (exceção vira nota "franquia indisponível: …"):
    /// sem número, e a mensagem técnica não vai para a tela.
    [Fact]
    public void Franquia_fora_do_ar_nao_vira_zero()
    {
        var dto = new DashboardOverviewDto();

        PublicacaoDeKpi.Publicar(dto, "agendados", null, "franquia indisponível: Spine aceita no máximo 99 dias");

        Assert.Empty(dto.KpiOverrides);
        Assert.Equal("A franquia não respondeu agora. Tente de novo em alguns minutos.", dto.KpisSemNumero["agendados"]);
    }

    [Fact]
    public void Sem_autorizacao_continua_no_cadeado()
    {
        var dto = new DashboardOverviewDto();

        PublicacaoDeKpi.Publicar(dto, "consultas", null, KpiNotes.SemAutorizacaoFranquia);

        Assert.Contains("consultas", dto.KpisSemAutorizacao);
        Assert.Empty(dto.KpisSemNumero);
        Assert.Empty(dto.KpiOverrides);
    }

    /// Zero medido continua sendo zero — "nenhum tratamento" é uma resposta.
    [Fact]
    public void Zero_medido_continua_publicado()
    {
        var dto = new DashboardOverviewDto();

        PublicacaoDeKpi.Publicar(dto, "receita", 0, "nenhum tratamento lançado no período");

        Assert.Equal(0d, dto.KpiOverrides["receita"]);
        Assert.Equal("nenhum tratamento lançado no período", dto.KpiNotas["receita"]);
        Assert.Empty(dto.KpisSemNumero);
    }

    /// A prévia das Configurações Técnicas (ComputeAsync) segue recebendo 0 — ela mostra a
    /// nota ao lado e não é o card do dono.
    [Fact]
    public async Task Previa_continua_recebendo_zero_com_a_nota()
    {
        using var db = NovoBanco();

        var (valor, _, nota) = await new KpiConfigService(db, null!, null!, NullLogger<KpiConfigService>.Instance)
            .ComputeAsync(Tenant, Unidade, KpiSourceTypes.Franquia,
                JsonDocument.Parse("""{"metric":"receita"}""").RootElement,
                DateTime.Parse("2026-09-01"), DateTime.Parse("2026-09-02"), null, "receita");

        Assert.Equal(0d, valor);
        Assert.Equal(KpiNotes.CruzamentoNaoRodou, nota);
    }
}
