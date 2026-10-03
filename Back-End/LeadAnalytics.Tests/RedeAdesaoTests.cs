using LeadAnalytics.Api.DTOs.Spine;
using LeadAnalytics.Api.Service.Spine;
using Xunit;

namespace LeadAnalytics.Tests;

/// <summary>
/// Adesão ao tratamento da rede. O que estes testes prendem:
///  1. LGPD — unidade com poucos pacientes sai OCULTA, sem nenhum número (nem a contagem de pacientes);
///     numa clínica pequena, "1 falta" aponta uma pessoa.
///  2. A conta da adesão: desmarcar e remarcar contam contra, horário futuro (agendado/confirmado) fica fora.
///  3. Os totais da rede não somam unidade com erro nem unidade oculta.
///  4. A semana começa na segunda-feira, inclusive quando o dia cai no domingo.
/// </summary>
public class RedeAdesaoTests
{
    private static SpineAvaliacoesDto Sessoes(int pacientes, int atendido, int faltou, int desmarcado, int remarcado = 0, int agendado = 0,
        params SpineAvaliacoesPorDiaDto[] porDia)
    {
        var situacoes = new List<SpineSituacaoDto>
        {
            new(SpineApiClient.ScheduleStatus.Atendido, "Atendido", "realizado", atendido),
            new(SpineApiClient.ScheduleStatus.NaoCompareceu, "Não compareceu", "falta", faltou),
            new(SpineApiClient.ScheduleStatus.Desmarcado, "Desmarcado", "cancelado", desmarcado),
            new(SpineApiClient.ScheduleStatus.Remarcado, "Remarcado", "cancelado", remarcado),
            new(SpineApiClient.ScheduleStatus.Agendado, "Agendado", "pendente", agendado),
        }.Where(s => s.Total > 0).ToList();
        var total = atendido + faltou + desmarcado + remarcado + agendado;
        return new SpineAvaliacoesDto(new(2026, 9, 1), new(2026, 9, 30), total, atendido, total - agendado, 0, pacientes,
            false, 0, situacoes, porDia, []);
    }

    private static FranquiaTratamentosDto Trat(int emAndamento, int finalizado) => new()
    {
        Total = emAndamento + finalizado,
        PorSituacao =
        [
            new FranquiaTratamentoSituacao { Situacao = "EM ANDAMENTO", Quantidade = emAndamento },
            new FranquiaTratamentoSituacao { Situacao = "FINALIZADO", Quantidade = finalizado },
        ],
    };

    [Fact]
    public void Unidade_com_poucos_pacientes_sai_oculta_e_sem_nenhum_numero()
    {
        var u = SpineRedeAdesaoService.MontarUnidade(1, "Pequena", Sessoes(pacientes: 4, atendido: 20, faltou: 1, desmarcado: 0), Trat(3, 1));

        Assert.True(u.Oculto);
        Assert.Null(u.SessoesRealizadas);
        Assert.Null(u.Faltas);
        Assert.Null(u.Desmarcadas);
        Assert.Null(u.TaxaAdesao);
        Assert.Null(u.PacientesDistintos); // nem a contagem de pacientes vaza
        Assert.Null(u.TratamentosNoPeriodo);
        Assert.Null(u.TratamentosEmAndamento);
    }

    [Fact]
    public void No_limite_do_sigilo_a_unidade_aparece()
    {
        var u = SpineRedeAdesaoService.MontarUnidade(1, "No limite", Sessoes(SpineRedeAdesaoService.SigiloMinimo, 10, 0, 0), null);
        Assert.False(u.Oculto);
        Assert.Equal(5, u.PacientesDistintos);
        Assert.Equal(2.0, u.SessoesPorPaciente);
    }

    [Fact]
    public void Adesao_conta_desmarcada_e_remarcada_contra_e_ignora_horario_futuro()
    {
        // 30 atendidas de 40 resolvidas (4 faltas + 3 desmarcadas + 3 remarcadas); 12 agendadas pra frente ficam fora.
        var u = SpineRedeAdesaoService.MontarUnidade(1, "Serra", Sessoes(8, atendido: 30, faltou: 4, desmarcado: 3, remarcado: 3, agendado: 12), Trat(5, 2));

        Assert.Equal(30, u.SessoesRealizadas);
        Assert.Equal(4, u.Faltas);
        Assert.Equal(6, u.Desmarcadas);
        Assert.Equal(75.0, u.TaxaAdesao);
        Assert.Equal(3.8, u.SessoesPorPaciente);
        Assert.Equal(7, u.TratamentosNoPeriodo);
        Assert.Equal(5, u.TratamentosEmAndamento);
    }

    [Fact]
    public void Sem_tratamentos_da_franquia_a_agenda_ainda_aparece()
    {
        var u = SpineRedeAdesaoService.MontarUnidade(1, "Serra", Sessoes(8, 16, 0, 0), null);
        Assert.Equal(16, u.SessoesRealizadas);
        Assert.Null(u.TratamentosNoPeriodo);
        Assert.Null(u.TratamentosEmAndamento);
    }

    [Fact]
    public void Taxa_sem_nada_resolvido_e_zero_e_nao_divide_por_zero()
    {
        Assert.Equal(0d, SpineRedeAdesaoService.Taxa(0, 0, 0));
    }

    [Fact]
    public void Totais_da_rede_nao_somam_unidade_com_erro_nem_unidade_oculta()
    {
        var a = SpineRedeAdesaoService.MontarUnidade(1, "A", Sessoes(8, 30, 5, 5), Trat(4, 1));
        var b = SpineRedeAdesaoService.MontarUnidade(2, "B", Sessoes(8, 10, 0, 0), Trat(1, 0));
        var oculta = SpineRedeAdesaoService.MontarUnidade(3, "Pequena", Sessoes(2, 99, 99, 99), Trat(9, 9));
        var erro = new SpineRedeAdesaoUnidadeDto(4, "Fora do ar", false, null, null, null, null, null, null, null, null, "indisponível agora");

        var t = SpineRedeAdesaoService.MontarTotais([a, b, oculta, erro]);

        Assert.Equal(3, t.Unidades);              // A, B e a oculta (a com erro não conta)
        Assert.Equal(40, t.SessoesRealizadas);    // 30 + 10; a oculta fica fora
        Assert.Equal(5, t.Faltas);
        Assert.Equal(5, t.Desmarcadas);
        Assert.Equal(Math.Round(40d / 50 * 100, 1), t.TaxaAdesao);
        Assert.Equal(6, t.TratamentosNoPeriodo);
        Assert.Equal(5, t.TratamentosEmAndamento);
    }

    [Theory]
    [InlineData(2026, 9, 28, 2026, 9, 28)] // segunda é a própria segunda
    [InlineData(2026, 9, 30, 2026, 9, 28)] // quarta
    [InlineData(2026, 10, 4, 2026, 9, 28)] // domingo pertence à semana que começou na segunda anterior
    [InlineData(2026, 10, 5, 2026, 10, 5)] // a segunda seguinte abre outra semana
    public void Semana_comeca_na_segunda_inclusive_quando_o_dia_e_domingo(int ay, int am, int ad, int ey, int em, int ed)
    {
        Assert.Equal(new DateOnly(ey, em, ed), SpineRedeAdesaoService.InicioDaSemana(new DateOnly(ay, am, ad)));
    }

    [Fact]
    public void Serie_semanal_soma_as_unidades_e_deixa_a_pequena_de_fora()
    {
        var a = Sessoes(8, 5, 0, 0, 0, 0, new SpineAvaliacoesPorDiaDto(new(2026, 9, 28), 4, 3), new SpineAvaliacoesPorDiaDto(new(2026, 10, 1), 2, 2));
        var b = Sessoes(8, 5, 0, 0, 0, 0, new SpineAvaliacoesPorDiaDto(new(2026, 9, 29), 6, 5), new SpineAvaliacoesPorDiaDto(new(2026, 10, 6), 1, 1));
        var pequena = Sessoes(2, 5, 0, 0, 0, 0, new SpineAvaliacoesPorDiaDto(new(2026, 9, 28), 100, 100));

        var serie = SpineRedeAdesaoService.MontarSerieSemanal([a, b, pequena]);

        Assert.Equal(2, serie.Count);
        Assert.Equal(new DateOnly(2026, 9, 28), serie[0].SemanaInicio);
        Assert.Equal(12, serie[0].Horarios);   // 4 + 2 + 6; a pequena (100) não entra
        Assert.Equal(10, serie[0].Realizadas); // 3 + 2 + 5
        Assert.Equal(new DateOnly(2026, 10, 5), serie[1].SemanaInicio);
        Assert.Equal(1, serie[1].Horarios);
    }
}
