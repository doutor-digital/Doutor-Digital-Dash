using LeadAnalytics.Api.Controllers;
using Xunit;

namespace LeadAnalytics.Tests;

/// <summary>
/// A regra que decide em QUE MÊS um tratamento aparece no painel.
///
/// POR QUE ESTE ARQUIVO EXISTE
/// ---------------------------
/// A Kommo carimba a entrada na etapa com a hora do arraste e não deixa editar. Quando
/// a SDR migra cards retroativos, um mês inteiro de tratamento antigo desaba no dia da
/// migração: o dia vira o recorde histórico da unidade e os meses reais ficam vazios.
/// Ninguém desconfia de um número alto num dia de mutirão — por isso a trava tem que
/// ser testada, não conferida no olho.
///
/// Caso real: Imperatriz, 01/09/2026, migração dos tratamentos retroativos.
/// </summary>
public class DatacaoDeMigracaoTests
{
    private static MovimentoParaDatar Mov(
        int id, int leadId, DateTime? corrigidaAntes = null, string etapa = "EM TRATAMENTO") =>
        new(id, leadId, etapa, new DateTime(2026, 9, 1, 13, 20, 0, DateTimeKind.Utc), corrigidaAntes);

    private static Dictionary<int, DateOnly> Lancamentos(params (int Lead, string Dia)[] pares) =>
        pares.ToDictionary(p => p.Lead, p => DateOnly.Parse(p.Dia));

    /// O caso central: card arrastado hoje, tratamento lançado em maio → conta em maio.
    [Fact]
    public void Card_arrastado_hoje_recebe_a_data_da_franquia()
    {
        var (corrigir, ignorados) = DatacaoDeMigracao.Separar(
            new[] { Mov(1, 500) }, Lancamentos((500, "2026-05-14")));

        var (mov, nova) = Assert.Single(corrigir);
        Assert.Equal(1, mov.HistoryId);
        Assert.Equal(new DateOnly(2026, 5, 14), DateOnly.FromDateTime(nova));
        Assert.Empty(ignorados);
    }

    /// Meio-dia, não meia-noite: o painel conta em dia COMERCIAL, cuja janela não começa
    /// à meia-noite. Carimbar 00:00 jogaria metade das correções para o dia anterior.
    [Fact]
    public void Data_corrigida_cai_no_meio_do_dia_em_utc()
    {
        var d = DatacaoDeMigracao.MeioDoDia(new DateOnly(2026, 5, 14));

        Assert.Equal(new DateTime(2026, 5, 14, 15, 0, 0, DateTimeKind.Utc), d);
        Assert.Equal(DateTimeKind.Utc, d.Kind);
    }

    /// Correção feita por uma pessoa nunca é sobrescrita: ela sabia de algo que a
    /// franquia não conta.
    [Fact]
    public void Correcao_humana_existente_e_preservada()
    {
        var antes = new DateTime(2026, 4, 2, 15, 0, 0, DateTimeKind.Utc);

        var (corrigir, ignorados) = DatacaoDeMigracao.Separar(
            new[] { Mov(1, 500, corrigidaAntes: antes) }, Lancamentos((500, "2026-05-14")));

        Assert.Empty(corrigir);
        Assert.Empty(ignorados);
    }

    /// Sem tratamento casado na franquia não há data verdadeira — e inventar uma seria
    /// trocar um erro visível (conta hoje) por um invisível (conta no mês errado).
    [Fact]
    public void Card_sem_tratamento_na_franquia_nao_e_datado()
    {
        var (corrigir, ignorados) = DatacaoDeMigracao.Separar(
            new[] { Mov(1, 999) }, Lancamentos((500, "2026-05-14")));

        Assert.Empty(corrigir);
        Assert.Equal(1, ignorados.Count);
        Assert.Equal(999, ignorados[0].LeadIdInterno);
    }

    /// A SDR arrasta o mesmo lead por várias etapas no mutirão (COMPARECEU, NEGOCIAÇÃO,
    /// EM TRATAMENTO). Todas as entradas do dia são igualmente falsas e todas recebem a
    /// mesma data real — deixar uma para trás faria o funil não fechar.
    [Fact]
    public void Todas_as_etapas_do_mesmo_lead_sao_datadas()
    {
        var (corrigir, _) = DatacaoDeMigracao.Separar(
            new[]
            {
                Mov(1, 500, etapa: "COMPARECEU"),
                Mov(2, 500, etapa: "NEGOCIAÇÃO"),
                Mov(3, 500, etapa: "EM TRATAMENTO"),
            },
            Lancamentos((500, "2026-05-14")));

        Assert.Equal(3, corrigir.Count);
        Assert.All(corrigir, c => Assert.Equal(new DateOnly(2026, 5, 14), DateOnly.FromDateTime(c.Nova)));
    }

    /// Leads diferentes recebem cada um a data do SEU tratamento.
    [Fact]
    public void Cada_lead_recebe_a_propria_data()
    {
        var (corrigir, ignorados) = DatacaoDeMigracao.Separar(
            new[] { Mov(1, 500), Mov(2, 501), Mov(3, 777) },
            Lancamentos((500, "2026-05-14"), (501, "2026-07-30")));

        Assert.Equal(new DateOnly(2026, 5, 14), DateOnly.FromDateTime(corrigir[0].Nova));
        Assert.Equal(new DateOnly(2026, 7, 30), DateOnly.FromDateTime(corrigir[1].Nova));
        Assert.Equal(777, Assert.Single(ignorados).LeadIdInterno);
    }

    /// Nada para fazer não pode virar exceção — o mutirão pode terminar sem sobra.
    [Fact]
    public void Lote_vazio_nao_explode()
    {
        var (corrigir, ignorados) = DatacaoDeMigracao.Separar(
            Array.Empty<MovimentoParaDatar>(), Lancamentos((500, "2026-05-14")));

        Assert.Empty(corrigir);
        Assert.Empty(ignorados);
    }

    /// Paciente que voltou para operar outra hérnia tem dois tratamentos. O card entrou
    /// em EM TRATAMENTO pela primeira vez por causa do PRIMEIRO — é essa data que vale.
    /// (A escolha do mínimo é feita na consulta; aqui fica o contrato que ela alimenta:
    /// um lead, uma data.)
    [Fact]
    public void Lead_com_dois_tratamentos_usa_a_data_do_primeiro()
    {
        var lancamentos = new[] { new DateOnly(2026, 7, 30), new DateOnly(2026, 5, 14) };
        var mapa = new Dictionary<int, DateOnly> { [500] = lancamentos.Min() };

        var (corrigir, _) = DatacaoDeMigracao.Separar(new[] { Mov(1, 500) }, mapa);

        Assert.Equal(new DateOnly(2026, 5, 14), DateOnly.FromDateTime(Assert.Single(corrigir).Nova));
    }
}
