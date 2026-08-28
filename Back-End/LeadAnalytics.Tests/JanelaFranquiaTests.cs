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
}
