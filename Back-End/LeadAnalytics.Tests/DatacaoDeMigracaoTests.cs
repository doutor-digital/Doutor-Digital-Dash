using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.Models;
using LeadAnalytics.Api.Service;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LeadAnalytics.Tests;

/// <summary>
/// A regra que decide em QUE MÊS um tratamento aparece no painel — e o que a SDR pode
/// carimbar sozinha.
///
/// POR QUE ESTE ARQUIVO EXISTE
/// ---------------------------
/// A Kommo carimba a entrada na etapa com a hora do arraste e não deixa editar. Num
/// mutirão de cards antigos, um mês inteiro desaba no dia da migração: o dia vira o
/// recorde histórico da unidade e os meses reais ficam vazios. Ninguém desconfia de um
/// número alto num dia de mutirão.
///
/// E como quem opera isto é a SDR — que tem meta —, a trava não pode ser "confiar":
/// a data não é digitada, vem do lançamento na franquia, e o AplicarAsync recalcula a
/// lista antes de gravar. O teste de id forjado abaixo é o que garante isso.
///
/// Caso real: Imperatriz, 01/09/2026, migração dos tratamentos retroativos.
/// </summary>
public class DatacaoDeMigracaoTests
{
    private const int Tenant = 1;
    private const int Unidade = 15;
    private static readonly DateOnly De = new(2026, 1, 1);
    // A janela de lançamento vai ATÉ HOJE de propósito: se parasse ontem, o tratamento
    // lançado hoje não seria encontrado e o trabalho normal do dia apareceria como
    // "sem vínculo" — um alarme falso por dia, todo dia.
    private static readonly DateOnly Ate = new(2026, 9, 30);
    private static readonly DateTime Mutirao = new(2026, 9, 1, 13, 20, 0, DateTimeKind.Utc);

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
            .UseInMemoryDatabase("datacao-" + nome)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics
                .InMemoryEventId.TransactionIgnoredWarning))
            .Options, new UsuarioNulo());

    private static DatacaoDeMigracaoService Servico(AppDbContext db) =>
        new(db, NullLogger<DatacaoDeMigracaoService>.Instance);

    /// Monta um lead, o vínculo com o tratamento da franquia e a movimentação do card.
    private static void Semear(
        AppDbContext db, int leadId, string? lancadoEm, DateTime arrastadoEm,
        string etapa = "EM TRATAMENTO", DateTime? jaCorrigido = null, int historyId = 0)
    {
        if (!db.Leads.Any(l => l.Id == leadId))
        {
            db.Leads.Add(new Lead
            {
                Id = leadId, ExternalId = 900_000 + leadId, Name = $"Paciente {leadId}",
                Phone = $"6399000{leadId:0000}", TenantId = Tenant, UnitId = Unidade,
                CreatedAt = arrastadoEm, UpdatedAt = arrastadoEm, Status = "active",
            });
        }

        if (lancadoEm is not null)
        {
            db.FranquiaLeadLinks.Add(new FranquiaLeadLink
            {
                UnitId = Unidade, IdTreatment = 100_000 + db.FranquiaLeadLinks.Count(),
                DiaLancamento = DateOnly.Parse(lancadoEm), LeadId = 900_000 + leadId,
                Paciente = $"Paciente {leadId}", AtualizadoEm = DateTime.UtcNow,
            });
        }

        db.LeadStageHistories.Add(new LeadStageHistory
        {
            Id = historyId == 0 ? db.LeadStageHistories.Count() + 1 : historyId,
            LeadId = leadId, StageId = etapa.GetHashCode() & 0x7fffffff, StageLabel = etapa,
            ChangedAt = arrastadoEm, EntrySource = LeadStageHistory.SourceWebhook,
            CorrectedChangedAt = jaCorrigido,
        });
        db.SaveChanges();
    }

    private static Task<PreviaDeMigracao> Prever(AppDbContext db) =>
        Servico(db).PreverAsync(Unidade, De, Ate, Mutirao.Date, Mutirao.AddHours(6));

    // ─── O caso central ─────────────────────────────────────────────────────────

    /// Card arrastado hoje, tratamento lançado em maio → conta em maio.
    [Fact]
    public async Task Card_de_mutirao_recebe_a_data_da_franquia()
    {
        using var db = NovoBanco();
        Semear(db, 500, "2026-05-14", Mutirao);

        var p = await Prever(db);

        var m = Assert.Single(p.Datar);
        Assert.Equal(new DateOnly(2026, 5, 14), m.LancadoEm);
        Assert.Empty(p.SemVinculo);
    }

    /// Aplicar grava a data e a trilha de auditoria — quem carimbou fica registrado.
    [Fact]
    public async Task Aplicar_grava_data_e_trilha_de_auditoria()
    {
        using var db = NovoBanco();
        Semear(db, 500, "2026-05-14", Mutirao);

        var n = await Servico(db).AplicarAsync(
            Unidade, De, Ate, Mutirao.Date, Mutirao.AddHours(6), null, "sdr@clinica.com", 7);

        Assert.Equal(1, n);
        var h = db.LeadStageHistories.Single();
        Assert.Equal(new DateTime(2026, 5, 14, 15, 0, 0, DateTimeKind.Utc), h.CorrectedChangedAt);
        Assert.Equal("sdr@clinica.com", h.CorrectedByEmail);
        Assert.Equal(7, h.CorrectedByUserId);
        Assert.Equal(DatacaoDeMigracaoService.Motivo, h.CorrectionReason);
    }

    // ─── A trava que torna isto seguro na mão de quem tem meta ──────────────────

    /// O teste que sustenta a permissão da SDR: mandar um history_id que a prévia NÃO
    /// autorizou não carimba nada. Sem isto, a tela viraria "edite qualquer data".
    [Fact]
    public async Task Id_forjado_fora_da_previa_nao_carimba_nada()
    {
        using var db = NovoBanco();
        Semear(db, 500, "2026-05-14", Mutirao, historyId: 1);
        // Card sem tratamento na franquia: a prévia manda para "sem vínculo".
        Semear(db, 777, null, Mutirao, historyId: 2);

        var n = await Servico(db).AplicarAsync(
            Unidade, De, Ate, Mutirao.Date, Mutirao.AddHours(6),
            apenasEstes: new[] { 2 }, "sdr@clinica.com", 7);

        Assert.Equal(0, n);
        Assert.Null(db.LeadStageHistories.Single(h => h.Id == 2).CorrectedChangedAt);
    }

    /// Sem tratamento casado não há data verdadeira — inventar uma seria trocar um erro
    /// visível (conta hoje) por um invisível (conta no mês errado).
    [Fact]
    public async Task Card_sem_tratamento_na_franquia_nao_e_datado()
    {
        using var db = NovoBanco();
        Semear(db, 777, null, Mutirao);

        var p = await Prever(db);

        Assert.Empty(p.Datar);
        Assert.Equal(777, Assert.Single(p.SemVinculo).LeadIdInterno);
    }

    /// Correção feita por uma pessoa nunca é sobrescrita: ela sabia de algo que a
    /// franquia não conta.
    [Fact]
    public async Task Correcao_humana_existente_e_preservada()
    {
        using var db = NovoBanco();
        var antes = new DateTime(2026, 4, 2, 15, 0, 0, DateTimeKind.Utc);
        Semear(db, 500, "2026-05-14", Mutirao, jaCorrigido: antes);

        var p = await Prever(db);
        var n = await Servico(db).AplicarAsync(
            Unidade, De, Ate, Mutirao.Date, Mutirao.AddHours(6), null, "sdr@clinica.com", 7);

        Assert.Empty(p.Datar);
        Assert.Equal(0, n);
        Assert.Equal(antes, db.LeadStageHistories.Single().CorrectedChangedAt);
    }

    // ─── Ruído que a tela não pode mostrar ──────────────────────────────────────

    /// Trabalho normal do dia: o card entrou em EM TRATAMENTO no mesmo dia em que a
    /// franquia lançou. Não é migração e não pode aparecer em lista nenhuma — nem para
    /// datar (não muda nada) nem como pendência (não há nada errado). Encher a tela de
    /// linhas inócuas treina a pessoa a clicar sem ler.
    [Fact]
    public async Task Movimentacao_do_dia_a_dia_nao_aparece_como_migracao()
    {
        using var db = NovoBanco();
        Semear(db, 500, "2026-09-01", Mutirao);

        var p = await Prever(db);

        Assert.Empty(p.Datar);
        Assert.Empty(p.SemVinculo);
        Assert.Equal(1, p.MovimentacoesNaJanela);
    }

    /// Fora da janela do mutirão nada é tocado, mesmo com tratamento casado.
    [Fact]
    public async Task Movimentacao_fora_da_janela_fica_de_fora()
    {
        using var db = NovoBanco();
        Semear(db, 500, "2026-05-14", Mutirao.AddDays(-3));

        var p = await Prever(db);

        Assert.Empty(p.Datar);
        Assert.Equal(0, p.MovimentacoesNaJanela);
    }

    // ─── Detalhes que já quebraram na prática ───────────────────────────────────

    /// Meio-dia, não meia-noite: o painel conta em dia COMERCIAL, cuja janela não começa
    /// à meia-noite. Carimbar 00:00 jogaria metade das correções para o dia anterior.
    [Fact]
    public void Data_corrigida_cai_no_meio_do_dia_em_utc()
    {
        var d = DatacaoDeMigracaoService.MeioDoDia(new DateOnly(2026, 5, 14));

        Assert.Equal(new DateTime(2026, 5, 14, 15, 0, 0, DateTimeKind.Utc), d);
        Assert.Equal(DateTimeKind.Utc, d.Kind);
    }

    /// Paciente que voltou para operar outra hérnia tem dois tratamentos. Vale o mais
    /// antigo — a primeira entrada em EM TRATAMENTO pertence ao primeiro. Antes disso a
    /// rota morria com chave duplicada (lead 9954, Imperatriz).
    [Fact]
    public async Task Lead_com_dois_tratamentos_usa_a_data_do_primeiro()
    {
        using var db = NovoBanco();
        Semear(db, 500, "2026-07-30", Mutirao);
        db.FranquiaLeadLinks.Add(new FranquiaLeadLink
        {
            UnitId = Unidade, IdTreatment = 999_999, DiaLancamento = new DateOnly(2026, 5, 14),
            LeadId = 900_500, Paciente = "Paciente 500", AtualizadoEm = DateTime.UtcNow,
        });
        db.SaveChanges();

        var p = await Prever(db);

        Assert.Equal(new DateOnly(2026, 5, 14), Assert.Single(p.Datar).LancadoEm);
        Assert.Equal(1, p.LeadsComMaisDeUmTratamento);
    }

    /// A SDR arrasta o mesmo lead por várias etapas no mutirão. Todas as entradas do dia
    /// são igualmente falsas e todas recebem a mesma data real — deixar uma para trás
    /// faria o funil não fechar.
    [Fact]
    public async Task Todas_as_etapas_do_mesmo_lead_sao_datadas()
    {
        using var db = NovoBanco();
        Semear(db, 500, "2026-05-14", Mutirao, etapa: "COMPARECEU", historyId: 1);
        Semear(db, 500, null, Mutirao.AddMinutes(2), etapa: "NEGOCIAÇÃO", historyId: 2);
        Semear(db, 500, null, Mutirao.AddMinutes(4), etapa: "EM TRATAMENTO", historyId: 3);

        var p = await Prever(db);

        Assert.Equal(3, p.Datar.Count);
        Assert.All(p.Datar, m => Assert.Equal(new DateOnly(2026, 5, 14), m.LancadoEm));
    }

    /// Mutirão sem sobra não pode virar exceção.
    [Fact]
    public async Task Janela_sem_movimentacao_devolve_vazio()
    {
        using var db = NovoBanco();

        var p = await Prever(db);

        Assert.Empty(p.Datar);
        Assert.Empty(p.SemVinculo);
        Assert.Equal(0, p.MovimentacoesNaJanela);
    }

    /// O id da etapa tem de chegar junto: o RÓTULO não serve para agrupar. Medido na
    /// Imperatriz (01/09/2026), a mesma etapa 143 aparece gravada ora como
    /// "TRATAMENTO_CANCELADO", ora como "143" — agrupar por texto quebraria a lista em
    /// dois blocos da mesma coisa.
    [Fact]
    public async Task Movimento_carrega_o_id_da_etapa_e_nao_so_o_rotulo()
    {
        using var db = NovoBanco();
        Semear(db, 500, "2026-05-14", Mutirao, etapa: "EM TRATAMENTO");

        var p = await Prever(db);

        var m = Assert.Single(p.Datar);
        Assert.True(m.EtapaId > 0);
        Assert.Equal("EM TRATAMENTO", m.Etapa);
    }
}
