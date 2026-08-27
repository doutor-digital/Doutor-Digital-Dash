using LeadAnalytics.Api.Service;
using LeadAnalytics.Api.Service.Stages;
using Xunit;

namespace LeadAnalytics.Tests;

/// <summary>
/// A etapa "NÃO COMPARECEU" contém "COMPARECEU" dentro dela. Sem cobrir a grafia com
/// acento e espaço, o resolvedor caía no Contains("COMPARECEU") e gravava a falta como
/// comparecimento — medido em 26/08/2026: 100 no-shows contados como atendidos
/// (Marabá 54, Balsas 38, Imperatriz 3, Porto 2, Serra 2, Taubaté 1).
/// </summary>
public class EtapaNaoCompareceuTests
{
    [Theory]
    [InlineData("NÃO COMPARECEU")]      // nome real no Kommo das 12 unidades
    [InlineData("Não compareceu")]
    [InlineData("NAO COMPARECEU")]
    [InlineData("NAO_COMPARECEU")]      // funil antigo
    [InlineData("nao-compareceu")]
    [InlineData("06_FALTOU_CONSULTA")]  // funil antigo, por palavra-chave
    [InlineData("NO-SHOW")]
    public void Falta_nunca_vira_comparecimento(string nomeDaEtapa)
    {
        var resolvido = CanonicalStages.Resolve(nomeDaEtapa);

        Assert.Equal(CanonicalStages.NaoCompareceu, resolvido);
        Assert.NotEqual(CanonicalStages.Compareceu, resolvido);
    }

    [Theory]
    [InlineData("COMPARECEU")]
    [InlineData("Compareceu")]
    public void Quem_compareceu_continua_comparecendo(string nomeDaEtapa)
    {
        Assert.Equal(CanonicalStages.Compareceu, CanonicalStages.Resolve(nomeDaEtapa));
    }

    [Fact]
    public void A_falta_vira_a_etapa_certa_no_dashboard()
    {
        var canonico = CanonicalStages.Resolve("NÃO COMPARECEU");

        Assert.Equal(LeadStages.Faltou, CanonicalStages.ToLeadStage(canonico));
        Assert.NotEqual(LeadStages.Compareceu, CanonicalStages.ToLeadStage(canonico));
    }

    /// <summary>As outras etapas do funil 2026 não podem ter regredido com a mudança.</summary>
    [Theory]
    [InlineData("EM QUALIFICAÇÃO", "QUALIFICACAO")]
    [InlineData("AGENDADO", "AGENDADO_SEM_PAGAMENTO")]
    [InlineData("EM NEGOCIAÇÃO", "NEGOCIACAO")]
    [InlineData("PERDIDO", "PERDIDO")]
    [InlineData("GANHO / CONCLUIDO", "TRATAMENTO_FECHADO")]
    [InlineData("EM TRATAMENTO", "COMPARECEU_CONSULTA")]
    [InlineData("TRATAMENTO CANCELADO", "TRATAMENTO_CANCELADO")]
    [InlineData("ALTA", "ALTA")]
    public void O_resto_do_funil_continua_igual(string nomeDaEtapa, string esperado)
    {
        Assert.Equal(esperado, CanonicalStages.Resolve(nomeDaEtapa));
    }
}
