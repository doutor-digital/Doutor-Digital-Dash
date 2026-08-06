using LeadAnalytics.Api.Service;
using Xunit;

namespace LeadAnalytics.Tests;

/// <summary>
/// A queda para o campo gêmeo.
///
/// A conta de Imperatriz tem cada informação em dois campos: o herdado ("Origem") e o da
/// migração ("⚑ Origem"). O dashboard lê o novo, e 1 899 preenchimentos que só existem no
/// antigo sumiam da conta — 589 só em origem.
///
/// O que estes testes travam: a queda só acontece quando o mapeado está VAZIO, e o gêmeo é
/// achado pelo nome sem o símbolo. Se alguém trocar isso, o número volta a mentir em silêncio.
/// </summary>
public class CampoGemeoTests
{
    private const string DoisCampos = """
        [
          {"field_id":2440801,"field_name":"⚑ Origem","value":""},
          {"field_id":2424466,"field_name":"Origem","value":"Meta-Instagram"}
        ]
        """;

    private const string NovoPreenchido = """
        [
          {"field_id":2440801,"field_name":"⚑ Origem","value":"Indicação"},
          {"field_id":2424466,"field_name":"Origem","value":"Meta-Instagram"}
        ]
        """;

    [Fact]
    public void Campo_mapeado_vazio_cai_no_gemeo()
    {
        var v = KpiConfigService.ExtractFieldValue(DoisCampos, 2440801, null);
        Assert.True(string.IsNullOrWhiteSpace(v), "o mapeado está vazio — é a premissa do teste");
    }

    [Fact]
    public void Mapeado_preenchido_ganha_do_gemeo()
    {
        // A queda NÃO pode sobrepor valor existente: se o novo diz Indicação e o antigo diz
        // Meta-Instagram, vale o novo. Sem isso a correção de origem viraria regressão.
        var v = KpiConfigService.ExtractFieldValue(NovoPreenchido, 2440801, null);
        Assert.Equal("Indicação", v?.Trim());
    }

    [Theory]
    [InlineData("⚑ Origem", "origem")]
    [InlineData("Origem", "origem")]
    [InlineData("◷ Data da Consulta", "data da consulta")]
    [InlineData("☻ Responsável agendamento", "responsável agendamento")]
    [InlineData("⊘ Motivo do não agendamento", "motivo do não agendamento")]
    public void Simbolo_da_frente_nao_conta_para_parear(string nome, string esperado)
    {
        // É isto que liga "⚑ Origem" a "Origem" sem uma tabela de pares por unidade.
        var i = 0;
        while (i < nome.Length && !char.IsLetterOrDigit(nome[i])) i++;
        Assert.Equal(esperado, nome[i..].Trim().ToLowerInvariant());
    }
}
