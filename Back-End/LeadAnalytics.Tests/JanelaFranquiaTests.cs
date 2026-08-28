using LeadAnalytics.Api.Service;
using Xunit;

namespace LeadAnalytics.Tests;

/// <summary>
/// A tela manda o período em "dia comercial", que vira às 19h da véspera: pedir 28/08
/// chega como [27/08 19:00, 28/08 19:00) local. A regra existe para LEAD — quem chega às
/// 20h conta para o dia seguinte — e não vale para agenda de clínica nem para lançamento
/// de tratamento, que são eventos de calendário.
///
/// O DEFEITO QUE ESTES TESTES TRAVAM
/// ---------------------------------
/// Medido em 28/08/2026, Marabá: o card de Tratamentos mostrava 3 e a tela da franquia
/// mostrava 0. Os três existiam — LUIZ CARLOS, MARIA SIMONE e JOSE DOS REIS — mas eram
/// todos lançados em 27/08. O backend fazia DateOnly.FromDateTime(from) e consultava a
/// franquia por 27..28.
///
/// Número do dia errado é pior que número inventado: ele parece certo.
///
/// As datas aqui são UTC porque é assim que chegam ao serviço; o horário local da clínica
/// é UTC−3, então 19:00 local = 22:00Z e a meia-noite local = 03:00Z.
/// </summary>
public class JanelaFranquiaTests
{
    private static DateTime Utc(int ano, int mes, int dia, int hora) =>
        new(ano, mes, dia, hora, 0, 0, DateTimeKind.Utc);

    /// O caso real de Marabá: "Dia 28/08" na tela.
    [Fact]
    public void Dia_comercial_vira_o_dia_de_calendario_escolhido()
    {
        var (de, ate) = KpiConfigService.JanelaDaFranquia(
            Utc(2026, 8, 27, 22),   // 27/08 19:00 local — início do dia comercial de 28/08
            Utc(2026, 8, 28, 22));  // 28/08 19:00 local — fim

        Assert.Equal(new DateOnly(2026, 8, 28), de);
        Assert.Equal(new DateOnly(2026, 8, 28), ate);
    }

    /// A conversão antiga: prova que o dia 27 entrava na conta.
    [Fact]
    public void O_dia_anterior_nao_entra_mais_na_janela()
    {
        var (de, _) = KpiConfigService.JanelaDaFranquia(
            Utc(2026, 8, 27, 22), Utc(2026, 8, 28, 22));

        Assert.NotEqual(new DateOnly(2026, 8, 27), de);
    }

    /// Janela já alinhada ao calendário (meia-noite local) não pode ser deslocada.
    [Fact]
    public void Janela_de_calendario_fica_no_lugar()
    {
        var (de, ate) = KpiConfigService.JanelaDaFranquia(
            Utc(2026, 8, 28, 3),    // 28/08 00:00 local
            Utc(2026, 8, 29, 3));   // 29/08 00:00 local (fim exclusivo)

        Assert.Equal(new DateOnly(2026, 8, 28), de);
        Assert.Equal(new DateOnly(2026, 8, 28), ate);
    }

    /// O preset "Mês": 30/07 a 28/08 em dia comercial.
    [Fact]
    public void Mes_comercial_cobre_do_primeiro_ao_ultimo_dia()
    {
        var (de, ate) = KpiConfigService.JanelaDaFranquia(
            Utc(2026, 7, 29, 22),   // véspera de 30/07, 19h local
            Utc(2026, 8, 28, 22));  // 28/08 19h local

        Assert.Equal(new DateOnly(2026, 7, 30), de);
        Assert.Equal(new DateOnly(2026, 8, 28), ate);
    }

    /// Virada de mês: o dia comercial de 01/08 começa em 31/07.
    [Fact]
    public void Virada_de_mes_nao_puxa_o_mes_anterior()
    {
        var (de, ate) = KpiConfigService.JanelaDaFranquia(
            Utc(2026, 7, 31, 22), Utc(2026, 8, 1, 22));

        Assert.Equal(new DateOnly(2026, 8, 1), de);
        Assert.Equal(new DateOnly(2026, 8, 1), ate);
    }

    /// Janela invertida ou degenerada não pode devolver fim antes do início.
    [Fact]
    public void Fim_nunca_fica_antes_do_inicio()
    {
        var (de, ate) = KpiConfigService.JanelaDaFranquia(
            Utc(2026, 8, 28, 22), Utc(2026, 8, 28, 22));

        Assert.True(ate >= de);
    }

    // ─── Presets da tela: dia, semana, mês, mês e meio ──────────────────────────

    /// A tela oferece Dia, Semana, Mês e períodos livres. Todos chegam em dia
    /// comercial, e todos têm de virar exatamente o intervalo de calendário que a
    /// pessoa escolheu — inclusive o primeiro e o último dia.
    [Theory]
    // dia  | início comercial (véspera 19h local = 22:00Z) | fim (19h local)        | de esperado | ate esperado
    [InlineData("2026-08-27T22:00", "2026-08-28T22:00", "2026-08-28", "2026-08-28")] // 1 dia
    [InlineData("2026-08-21T22:00", "2026-08-28T22:00", "2026-08-22", "2026-08-28")] // 7 dias
    [InlineData("2026-07-29T22:00", "2026-08-28T22:00", "2026-07-30", "2026-08-28")] // 30 dias
    [InlineData("2026-07-14T22:00", "2026-08-28T22:00", "2026-07-15", "2026-08-28")] // 45 dias (mês e meio)
    [InlineData("2026-07-31T22:00", "2026-08-31T22:00", "2026-08-01", "2026-08-31")] // mês cheio
    [InlineData("2025-12-31T22:00", "2026-01-31T22:00", "2026-01-01", "2026-01-31")] // virada de ano
    [InlineData("2026-02-28T22:00", "2026-03-31T22:00", "2026-03-01", "2026-03-31")] // ano bissexto → março
    public void Todos_os_presets_viram_o_intervalo_escolhido(
        string inicio, string fim, string deEsperado, string ateEsperado)
    {
        var (de, ate) = KpiConfigService.JanelaDaFranquia(
            DateTime.Parse(inicio + "Z").ToUniversalTime(),
            DateTime.Parse(fim + "Z").ToUniversalTime());

        Assert.Equal(DateOnly.Parse(deEsperado), de);
        Assert.Equal(DateOnly.Parse(ateEsperado), ate);
    }

    /// O tamanho da janela em dias tem de ser preservado — uma semana não pode virar
    /// 6 nem 8 dias por causa da conversão.
    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(14)]
    [InlineData(30)]
    [InlineData(45)]
    [InlineData(90)]
    public void O_tamanho_da_janela_nao_muda_na_conversao(int dias)
    {
        // Janela comercial de `dias` terminando em 28/08.
        var fim = new DateTime(2026, 8, 28, 22, 0, 0, DateTimeKind.Utc);
        var inicio = fim.AddDays(-dias);

        var (de, ate) = KpiConfigService.JanelaDaFranquia(inicio, fim);

        Assert.Equal(dias - 1, ate.DayNumber - de.DayNumber);
    }

    /// Períodos vizinhos não podem se sobrepor: o fim de um é o dia anterior ao
    /// início do próximo. Sem isso, um tratamento seria contado em dois meses.
    [Fact]
    public void Meses_vizinhos_nao_se_sobrepoem()
    {
        var (_, ateJulho) = KpiConfigService.JanelaDaFranquia(
            new DateTime(2026, 6, 30, 22, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 7, 31, 22, 0, 0, DateTimeKind.Utc));

        var (deAgosto, _) = KpiConfigService.JanelaDaFranquia(
            new DateTime(2026, 7, 31, 22, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 8, 31, 22, 0, 0, DateTimeKind.Utc));

        Assert.Equal(new DateOnly(2026, 7, 31), ateJulho);
        Assert.Equal(new DateOnly(2026, 8, 1), deAgosto);
        Assert.True(deAgosto > ateJulho);
    }

    /// Horário customizado (a tela deixa escolher hora) não pode deslocar o dia.
    [Theory]
    [InlineData("2026-08-28T03:00", "2026-08-29T02:59")]  // dia inteiro local
    [InlineData("2026-08-28T10:00", "2026-08-28T22:00")]  // 07:00 às 19:00 local
    [InlineData("2026-08-28T13:00", "2026-08-28T20:00")]  // 10:00 às 17:00 local
    public void Recorte_por_hora_dentro_do_dia_nao_muda_o_dia(string inicio, string fim)
    {
        var (de, ate) = KpiConfigService.JanelaDaFranquia(
            DateTime.Parse(inicio + "Z").ToUniversalTime(),
            DateTime.Parse(fim + "Z").ToUniversalTime());

        Assert.Equal(new DateOnly(2026, 8, 28), de);
        Assert.Equal(new DateOnly(2026, 8, 28), ate);
    }

    // ─── Varredura: todo dia do ano e toda largura de janela ────────────────────

    /// Um caso por DIA de 2026: a janela comercial daquele dia tem de virar
    /// exatamente aquele dia de calendário. 365 casos.
    ///
    /// Existe porque erro de fuso não aparece no dia que você testou à mão — aparece
    /// no fim do mês, na virada do ano, no 29 de fevereiro. Varrer o ano inteiro é
    /// mais barato que descobrir em produção, que foi como descobrimos o primeiro.
    public static IEnumerable<object[]> TodosOsDiasDe2026()
    {
        var d = new DateOnly(2026, 1, 1);
        while (d.Year == 2026)
        {
            yield return new object[] { d.ToString("yyyy-MM-dd") };
            d = d.AddDays(1);
        }
    }

    [Theory]
    [MemberData(nameof(TodosOsDiasDe2026))]
    public void Qualquer_dia_do_ano_vira_ele_mesmo(string dia)
    {
        var alvo = DateOnly.Parse(dia);
        // Dia comercial: começa 19h local da véspera (22:00Z) e termina 19h local.
        var inicio = alvo.AddDays(-1).ToDateTime(new TimeOnly(22, 0), DateTimeKind.Utc);
        var fim = alvo.ToDateTime(new TimeOnly(22, 0), DateTimeKind.Utc);

        var (de, ate) = KpiConfigService.JanelaDaFranquia(inicio, fim);

        Assert.Equal(alvo, de);
        Assert.Equal(alvo, ate);
    }

    /// Uma janela de N dias terminando em cada mês do ano, para N de 1 a 90:
    /// o intervalo devolvido tem de ter exatamente N dias. 90 x 12 = 1.080 casos.
    public static IEnumerable<object[]> LargurasEMeses()
    {
        for (var mes = 1; mes <= 12; mes++)
            for (var n = 1; n <= 90; n++)
                yield return new object[] { mes, n };
    }

    [Theory]
    [MemberData(nameof(LargurasEMeses))]
    public void Janela_de_n_dias_devolve_n_dias(int mes, int dias)
    {
        // Termina no dia 15 do mês, 19h local.
        var fim = new DateTime(2026, mes, 15, 22, 0, 0, DateTimeKind.Utc);
        var inicio = fim.AddDays(-dias);

        var (de, ate) = KpiConfigService.JanelaDaFranquia(inicio, fim);

        Assert.Equal(dias - 1, ate.DayNumber - de.DayNumber);
        Assert.True(ate >= de);
    }

    /// Cada hora do dia como início de janela: nenhuma pode cair no dia anterior.
    /// 24 casos — o horário customizado da tela permite qualquer um deles.
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    [InlineData(4)] [InlineData(5)] [InlineData(6)] [InlineData(7)]
    [InlineData(8)] [InlineData(9)] [InlineData(10)] [InlineData(11)]
    [InlineData(12)] [InlineData(13)] [InlineData(14)] [InlineData(15)]
    [InlineData(16)] [InlineData(17)] [InlineData(18)] [InlineData(19)]
    [InlineData(20)] [InlineData(21)] [InlineData(22)] [InlineData(23)]
    public void Comeco_em_qualquer_hora_local_fica_no_dia_certo(int horaLocal)
    {
        // 28/08 às `horaLocal` no fuso da clínica → UTC = +3h.
        var inicio = new DateTime(2026, 8, 28, 0, 0, 0, DateTimeKind.Utc)
            .AddHours(3 + horaLocal);
        var fim = inicio.AddHours(1);

        var (de, ate) = KpiConfigService.JanelaDaFranquia(inicio, fim);

        // Depois das 19h local o dia comercial já é o seguinte — é a regra da tela.
        var esperado = horaLocal >= 19 ? new DateOnly(2026, 8, 29) : new DateOnly(2026, 8, 28);
        Assert.Equal(esperado, de);
        Assert.True(ate >= de);
    }
}
