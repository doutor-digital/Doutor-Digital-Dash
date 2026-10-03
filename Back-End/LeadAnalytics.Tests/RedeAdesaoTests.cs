using LeadAnalytics.Api.DTOs.Spine;
using LeadAnalytics.Api.Service.Spine;
using Xunit;

namespace LeadAnalytics.Tests;

/// <summary>
/// Adesão ao tratamento da rede. O que estes testes prendem:
///  1. LGPD — unidade com menos de 5 PESSOAS na conta sai oculta, sem nenhum número. Quem conta é quem tem
///     horário que entra na conta (remarcada não entra), e grafia diferente do mesmo nome não vira outra pessoa.
///  2. A conta da adesão: falta, desmarcada e horário passado sem baixa contam contra; remarcada não (gera
///     outro horário, que conta por si). Sem base, a taxa é nula — nunca um 0% vermelho.
///  3. A janela termina ONTEM no horário de Brasília, mesmo quando já é amanhã em UTC.
///  4. A série semanal só tem semanas inteiras dentro da janela.
/// </summary>
public class RedeAdesaoTests
{
    private static long _id = 1;

    private static SpineSchedule S(string paciente, int status, DateTime? quando = null) => new()
    {
        IdSchedule = _id++,
        ClientName = paciente,
        IdStatus = status,
        DateAttendance = quando ?? new DateTime(2026, 9, 15, 13, 0, 0, DateTimeKind.Utc),
    };

    private const int At = SpineApiClient.ScheduleStatus.Atendido;
    private const int Falta = SpineApiClient.ScheduleStatus.NaoCompareceu;
    private const int Desm = SpineApiClient.ScheduleStatus.Desmarcado;
    private const int Rem = SpineApiClient.ScheduleStatus.Remarcado;
    private const int Ag = SpineApiClient.ScheduleStatus.Agendado;
    private const int Conf = SpineApiClient.ScheduleStatus.Confirmado;

    private static List<SpineSchedule> Pacientes(int n, int status = At) =>
        Enumerable.Range(1, n).Select(i => S($"Paciente {i}", status)).ToList();

    [Fact]
    public void Unidade_com_poucas_pessoas_sai_oculta_e_sem_nenhum_numero()
    {
        var u = SpineRedeAdesaoService.MontarUnidade(1, "Pequena", Pacientes(4), 3, false);

        Assert.True(u.Oculto);
        Assert.Null(u.SessoesRealizadas);
        Assert.Null(u.Faltas);
        Assert.Null(u.Desmarcadas);
        Assert.Null(u.SemBaixa);
        Assert.Null(u.TaxaAdesao);
        Assert.Null(u.PacientesDistintos); // nem a contagem de pacientes vaza
        Assert.Null(u.TratamentosIniciados);
    }

    [Fact]
    public void Remarcada_nao_conta_como_pessoa_para_o_sigilo()
    {
        // 1 pessoa com falta + 4 que só remarcaram: a falta mostrada apontaria 1 pessoa. Tem que ocultar.
        var linhas = new List<SpineSchedule> { S("Ana", Falta) };
        linhas.AddRange(Enumerable.Range(1, 4).Select(i => S($"Remarcou {i}", Rem)));
        Assert.True(SpineRedeAdesaoService.MontarUnidade(1, "X", linhas, null, false).Oculto);
    }

    [Fact]
    public void Grafia_diferente_do_mesmo_nome_e_a_mesma_pessoa()
    {
        var linhas = Pacientes(4);
        linhas.Add(S("Paciente 1 ", At));       // espaço sobrando
        linhas.Add(S("PACIENTE  1", At));       // caixa e espaço duplo
        linhas.Add(S("Pacíente 1", At));        // acento
        Assert.True(SpineRedeAdesaoService.MontarUnidade(1, "X", linhas, null, false).Oculto);
        Assert.Equal("MARIA SOUZA", SpineRedeAdesaoService.NomeNormalizado("  María   Souza "));
    }

    [Fact]
    public void Adesao_conta_falta_desmarcada_e_sem_baixa_contra_e_deixa_remarcada_fora()
    {
        var linhas = new List<SpineSchedule>();
        for (var i = 0; i < 30; i++) linhas.Add(S($"P{i % 8}", At));
        for (var i = 0; i < 4; i++) linhas.Add(S($"P{i}", Falta));
        for (var i = 0; i < 3; i++) linhas.Add(S($"P{i}", Desm));
        for (var i = 0; i < 3; i++) linhas.Add(S($"P{i}", Rem));      // fora da conta
        linhas.Add(S("P5", Ag)); linhas.Add(S("P6", Ag)); linhas.Add(S("P7", Conf)); // passaram sem baixa

        var u = SpineRedeAdesaoService.MontarUnidade(1, "Serra", linhas, 7, false);

        Assert.False(u.Oculto);
        Assert.Equal(30, u.SessoesRealizadas);
        Assert.Equal(4, u.Faltas);
        Assert.Equal(3, u.Desmarcadas);
        Assert.Equal(3, u.SemBaixa);
        Assert.Equal(75.0, u.TaxaAdesao);           // 30 ÷ 40
        Assert.Equal(8, u.PacientesDistintos);
        Assert.Equal(3.8, u.SessoesPorPaciente);
        Assert.Equal(7, u.TratamentosIniciados);
    }

    [Fact]
    public void Sem_base_a_taxa_e_nula_e_nao_zero()
    {
        Assert.Null(SpineRedeAdesaoService.Taxa(0, 0, 0, 0));
        Assert.Equal(100.0, SpineRedeAdesaoService.Taxa(5, 0, 0, 0));
    }

    [Fact]
    public void Incompleto_chega_na_linha_da_unidade()
    {
        Assert.True(SpineRedeAdesaoService.MontarUnidade(1, "Grande", Pacientes(6), null, true).Incompleto);
    }

    [Fact]
    public void Totais_nao_somam_erro_nem_oculta_e_sem_nada_visivel_a_taxa_e_nula()
    {
        var a = SpineRedeAdesaoService.MontarUnidade(1, "A", Pacientes(8).Concat(Pacientes(2, Falta)).ToList(), 4, false);
        var b = SpineRedeAdesaoService.MontarUnidade(2, "B", Pacientes(6), 1, false);
        var oculta = SpineRedeAdesaoService.MontarUnidade(3, "Pequena", Pacientes(2), 9, false);
        var erro = new SpineRedeAdesaoUnidadeDto(4, "Fora", false, null, null, null, null, null, null, null, null, false, "indisponível agora");

        var t = SpineRedeAdesaoService.MontarTotais([a, b, oculta, erro]);
        Assert.Equal(3, t.Unidades);          // A, B e a oculta; a com erro não conta
        Assert.Equal(14, t.SessoesRealizadas); // 8 + 6
        Assert.Equal(2, t.Faltas);
        Assert.Equal(Math.Round(14d / 16 * 100, 1), t.TaxaAdesao);
        Assert.Equal(5, t.TratamentosIniciados);

        Assert.Null(SpineRedeAdesaoService.MontarTotais([oculta, erro]).TaxaAdesao);
    }

    [Fact]
    public void Janela_termina_ontem_no_horario_de_brasilia_mesmo_depois_das_21h()
    {
        // 03/10 01:00 UTC = 02/10 22:00 em Brasília: "ontem" é 01/10, não 02/10.
        var (de, ate) = SpineRedeAdesaoService.Janela(30, new DateTime(2026, 10, 3, 1, 0, 0, DateTimeKind.Utc));
        Assert.Equal(new DateOnly(2026, 10, 1), ate);
        Assert.Equal(new DateOnly(2026, 9, 2), de);   // 30 dias, inclusivos
    }

    [Theory]
    [InlineData(2026, 9, 28, 2026, 9, 28)]
    [InlineData(2026, 9, 30, 2026, 9, 28)]
    [InlineData(2026, 10, 4, 2026, 9, 28)] // domingo pertence à semana que começou na segunda anterior
    [InlineData(2026, 10, 5, 2026, 10, 5)]
    public void Semana_comeca_na_segunda(int ay, int am, int ad, int ey, int em, int ed) =>
        Assert.Equal(new DateOnly(ey, em, ed), SpineRedeAdesaoService.InicioDaSemana(new DateOnly(ay, am, ad)));

    [Fact]
    public void Serie_so_tem_semanas_inteiras_dentro_da_janela()
    {
        // Janela 02/09 (quarta) a 01/10 (quarta). Semanas inteiras: 07/09, 14/09, 21/09. As pontas ficam fora.
        DateTime Dia(int m, int d) => new(2026, m, d, 13, 0, 0, DateTimeKind.Utc);
        var u1 = new List<SpineSchedule> { S("A", At, Dia(9, 3)), S("A", At, Dia(9, 8)), S("B", Falta, Dia(9, 9)), S("C", At, Dia(9, 22)), S("C", At, Dia(9, 30)) };
        var u2 = new List<SpineSchedule> { S("D", At, Dia(9, 10)), S("E", Rem, Dia(9, 11)), S("F", At, Dia(9, 16)) };

        var serie = SpineRedeAdesaoService.MontarSerieSemanal([u1, u2], new DateOnly(2026, 9, 2), new DateOnly(2026, 10, 1));

        Assert.Equal([new DateOnly(2026, 9, 7), new DateOnly(2026, 9, 14), new DateOnly(2026, 9, 21)], serie.Select(s => s.SemanaInicio));
        Assert.Equal(3, serie[0].Horarios);    // 08 (A), 09 (B falta), 10 (D); a remarcada do dia 11 fica fora
        Assert.Equal(2, serie[0].Realizadas);
        Assert.Equal(1, serie[1].Realizadas);
        Assert.Equal(1, serie[2].Horarios);    // 22/09; o dia 30 é da semana parcial do fim
    }
}
