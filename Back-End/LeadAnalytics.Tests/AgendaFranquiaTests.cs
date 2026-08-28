using LeadAnalytics.Api.Service.Spine;
using Xunit;

namespace LeadAnalytics.Tests;

/// <summary>
/// A seção "Operação clínica" lê a agenda do Doutor Hérnia. Tudo nela depende de duas
/// coisas darem certo: a conversão de fuso (o Spine devolve UTC, a clínica pensa em
/// horário local) e a classificação do desfecho de cada horário.
///
/// POR QUE ESTE ARQUIVO EXISTE
/// ---------------------------
/// Já apanhamos duas vezes de data neste painel: o KPI de tratamentos consultando o dia
/// errado por causa do dia comercial, e o semáforo recortando por criação do lead em vez
/// de entrada na etapa. Nos dois casos o número existia, era plausível, e estava errado —
/// que é o defeito mais caro de todos, porque ninguém desconfia.
///
/// Aqui o fuso é UTC−3 (America/São_Paulo, sem horário de verão desde 2019): meia-noite
/// local = 03:00Z, e 21:00 local = 00:00Z do dia seguinte.
/// </summary>
public class AgendaFranquiaTests
{
    private static DateTime Utc(int ano, int mes, int dia, int h, int min = 0) =>
        new(ano, mes, dia, h, min, 0, DateTimeKind.Utc);

    private static SpineSchedule Horario(long id, int status, DateTime quando, string? nome = null) =>
        new()
        {
            IdSchedule = id,
            IdStatus = status,
            StatusName = nome,
            DateAttendance = quando,
            ClientName = $"Paciente {id}",
        };

    private static readonly DateOnly Dia = new(2026, 8, 28);

    // ─── Fuso ───────────────────────────────────────────────────────────────────

    /// Meia-noite local é 03:00Z. Um minuto antes ainda é o dia anterior.
    [Theory]
    [InlineData(2026, 8, 28, 3, 0, "2026-08-28")]    // 00:00 local
    [InlineData(2026, 8, 28, 2, 59, "2026-08-27")]   // 23:59 local do dia 27
    [InlineData(2026, 8, 29, 2, 0, "2026-08-28")]    // 23:00 local do dia 28
    [InlineData(2026, 8, 28, 12, 0, "2026-08-28")]   // 09:00 local
    public void Dia_local_respeita_o_fuso_da_clinica(int a, int m, int d, int h, int min, string esperado)
    {
        Assert.Equal(DateOnly.Parse(esperado), SpineApiClient.DiaLocal(Utc(a, m, d, h, min)));
    }

    /// O horário das 23h local pertence ao dia 28, mesmo chegando como 29 em UTC.
    /// Sem isto, o card do dia perde o último atendimento da noite.
    [Fact]
    public void Atendimento_da_noite_nao_vaza_para_o_dia_seguinte()
    {
        var dto = SpineAvaliacoesService.Montar(Dia, Dia, new[]
        {
            Horario(1, SpineApiClient.ScheduleStatus.Atendido, Utc(2026, 8, 29, 2)), // 23:00 local do 28
        });

        var porDia = Assert.Single(dto.PorDia);
        Assert.Equal(Dia, porDia.Dia);
        Assert.Equal(1, porDia.Realizadas);
    }

    // ─── Contagem e desfecho ────────────────────────────────────────────────────

    /// Total conta TODOS os horários da janela, em qualquer situação.
    [Fact]
    public void Total_conta_todos_os_horarios()
    {
        var dto = SpineAvaliacoesService.Montar(Dia, Dia, new[]
        {
            Horario(1, SpineApiClient.ScheduleStatus.Atendido, Utc(2026, 8, 28, 12)),
            Horario(2, SpineApiClient.ScheduleStatus.NaoCompareceu, Utc(2026, 8, 28, 13)),
            Horario(3, SpineApiClient.ScheduleStatus.Desmarcado, Utc(2026, 8, 28, 14)),
            Horario(4, SpineApiClient.ScheduleStatus.Agendado, Utc(2026, 8, 28, 20)),
        });

        Assert.Equal(4, dto.Total);
        Assert.Equal(1, dto.Realizadas);
    }

    /// O que ainda não aconteceu (agendado/confirmado) sai do DENOMINADOR: horário de
    /// hoje à noite não é acerto nem erro, e contá-lo derrubaria a taxa sem motivo.
    [Fact]
    public void Horario_que_ainda_nao_chegou_nao_entra_na_taxa()
    {
        var dto = SpineAvaliacoesService.Montar(Dia, Dia, new[]
        {
            Horario(1, SpineApiClient.ScheduleStatus.Atendido, Utc(2026, 8, 28, 12)),
            Horario(2, SpineApiClient.ScheduleStatus.Agendado, Utc(2026, 8, 28, 21)),
            Horario(3, SpineApiClient.ScheduleStatus.Confirmado, Utc(2026, 8, 28, 22)),
        });

        Assert.Equal(3, dto.Total);
        Assert.Equal(1, dto.Resolvidas);
        Assert.Equal(100d, dto.TaxaComparecimento);
    }

    /// Desmarcado ENTRA no denominador: é agenda que a clínica reservou e não usou.
    [Fact]
    public void Desmarcado_conta_contra_a_taxa()
    {
        var dto = SpineAvaliacoesService.Montar(Dia, Dia, new[]
        {
            Horario(1, SpineApiClient.ScheduleStatus.Atendido, Utc(2026, 8, 28, 12)),
            Horario(2, SpineApiClient.ScheduleStatus.Desmarcado, Utc(2026, 8, 28, 13)),
        });

        Assert.Equal(2, dto.Resolvidas);
        Assert.Equal(50d, dto.TaxaComparecimento);
    }

    /// Agenda só com horários futuros não pode virar divisão por zero nem 100%.
    [Fact]
    public void Agenda_so_com_futuro_nao_inventa_taxa()
    {
        var dto = SpineAvaliacoesService.Montar(Dia, Dia, new[]
        {
            Horario(1, SpineApiClient.ScheduleStatus.Agendado, Utc(2026, 8, 28, 20)),
            Horario(2, SpineApiClient.ScheduleStatus.Confirmado, Utc(2026, 8, 28, 21)),
        });

        Assert.Equal(0, dto.Resolvidas);
        Assert.Equal(0d, dto.TaxaComparecimento);
    }

    /// Agenda vazia devolve zeros, não explode.
    [Fact]
    public void Agenda_vazia_devolve_zeros()
    {
        var dto = SpineAvaliacoesService.Montar(Dia, Dia, Array.Empty<SpineSchedule>());

        Assert.Equal(0, dto.Total);
        Assert.Equal(0, dto.Realizadas);
        Assert.Equal(0d, dto.TaxaComparecimento);
        Assert.Empty(dto.PorSituacao);
    }

    // ─── Qualidade do dado ──────────────────────────────────────────────────────

    /// O padrão que já achamos em produção: a recepção usa DESMARCADO para tudo,
    /// inclusive para quem simplesmente não veio, e a falta some do relatório.
    [Fact]
    public void Alerta_liga_quando_desmarcado_vira_guarda_chuva()
    {
        var dto = SpineAvaliacoesService.Montar(Dia, Dia, new[]
        {
            Horario(1, SpineApiClient.ScheduleStatus.Desmarcado, Utc(2026, 8, 28, 12)),
            Horario(2, SpineApiClient.ScheduleStatus.Desmarcado, Utc(2026, 8, 28, 13)),
            Horario(3, SpineApiClient.ScheduleStatus.Desmarcado, Utc(2026, 8, 28, 14)),
            Horario(4, SpineApiClient.ScheduleStatus.Atendido, Utc(2026, 8, 28, 15)),
        });

        Assert.True(dto.AlertaQualidadeDados);
    }

    /// Com faltas registradas na proporção esperada, não há alerta — senão vira ruído.
    [Fact]
    public void Alerta_nao_liga_quando_a_falta_esta_registrada()
    {
        var dto = SpineAvaliacoesService.Montar(Dia, Dia, new[]
        {
            Horario(1, SpineApiClient.ScheduleStatus.Desmarcado, Utc(2026, 8, 28, 12)),
            Horario(2, SpineApiClient.ScheduleStatus.Desmarcado, Utc(2026, 8, 28, 13)),
            Horario(3, SpineApiClient.ScheduleStatus.Desmarcado, Utc(2026, 8, 28, 14)),
            Horario(4, SpineApiClient.ScheduleStatus.NaoCompareceu, Utc(2026, 8, 28, 15)),
            Horario(5, SpineApiClient.ScheduleStatus.NaoCompareceu, Utc(2026, 8, 28, 16)),
        });

        Assert.False(dto.AlertaQualidadeDados);
    }

    /// Situação que a franquia inventar depois não pode sumir da conta.
    [Fact]
    public void Status_desconhecido_aparece_em_vez_de_sumir()
    {
        var dto = SpineAvaliacoesService.Montar(Dia, Dia, new[]
        {
            Horario(1, 99, Utc(2026, 8, 28, 12), "EM ESPERA"),
        });

        Assert.Equal(1, dto.Total);
        var s = Assert.Single(dto.PorSituacao);
        Assert.Equal("EM ESPERA", s.Nome);
        Assert.Equal("desconhecido", s.Grupo);
    }

    /// Situação sem nenhum horário não polui a lista.
    [Fact]
    public void Situacao_zerada_nao_aparece()
    {
        var dto = SpineAvaliacoesService.Montar(Dia, Dia, new[]
        {
            Horario(1, SpineApiClient.ScheduleStatus.Atendido, Utc(2026, 8, 28, 12)),
        });

        Assert.Single(dto.PorSituacao);
        Assert.All(dto.PorSituacao, s => Assert.True(s.Total > 0));
    }

    // ─── Janela de vários dias ──────────────────────────────────────────────────

    /// Janela de vários dias separa por dia local, e cada dia mantém o próprio desfecho.
    [Fact]
    public void Janela_de_varios_dias_separa_por_dia_local()
    {
        var de = new DateOnly(2026, 8, 27);
        var dto = SpineAvaliacoesService.Montar(de, Dia, new[]
        {
            Horario(1, SpineApiClient.ScheduleStatus.Atendido, Utc(2026, 8, 27, 12)),
            Horario(2, SpineApiClient.ScheduleStatus.Atendido, Utc(2026, 8, 28, 12)),
            Horario(3, SpineApiClient.ScheduleStatus.NaoCompareceu, Utc(2026, 8, 28, 13)),
        });

        Assert.Equal(2, dto.PorDia.Count);
        Assert.Equal(1, dto.PorDia.Single(d => d.Dia == de).Realizadas);
        Assert.Equal(2, dto.PorDia.Single(d => d.Dia == Dia).Total);
        Assert.Equal(1, dto.PorDia.Single(d => d.Dia == Dia).Realizadas);
    }

    /// Pacientes distintos não contam duas vezes quando alguém tem dois horários.
    [Fact]
    public void Paciente_com_dois_horarios_conta_uma_vez_em_distintos()
    {
        var mesmo = Horario(1, SpineApiClient.ScheduleStatus.Atendido, Utc(2026, 8, 28, 12));
        var outro = Horario(2, SpineApiClient.ScheduleStatus.Atendido, Utc(2026, 8, 28, 15));
        outro.ClientName = mesmo.ClientName;

        var dto = SpineAvaliacoesService.Montar(Dia, Dia, new[] { mesmo, outro });

        Assert.Equal(2, dto.Total);
        Assert.Equal(1, dto.PacientesDistintos);
    }

    /// Horário sem data não pode derrubar o agrupamento por dia.
    [Fact]
    public void Horario_sem_data_nao_quebra_o_agrupamento()
    {
        var semData = Horario(2, SpineApiClient.ScheduleStatus.Atendido, Utc(2026, 8, 28, 12));
        semData.DateAttendance = null;

        var dto = SpineAvaliacoesService.Montar(Dia, Dia, new[]
        {
            Horario(1, SpineApiClient.ScheduleStatus.Atendido, Utc(2026, 8, 28, 12)),
            semData,
        });

        Assert.Equal(2, dto.Total);
        Assert.Single(dto.PorDia);
    }
}
