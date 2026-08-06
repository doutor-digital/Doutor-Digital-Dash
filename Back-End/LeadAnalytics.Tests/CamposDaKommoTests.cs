using LeadAnalytics.Api.Service;
using Xunit;

namespace LeadAnalytics.Tests;

/// <summary>
/// A leitura do cartão da Kommo.
///
/// POR QUE ESTES TESTES EXISTEM
/// ----------------------------
/// Todo número errado que apareceu no dashboard nasceu aqui: campo lido do id errado, data
/// que chegou como carimbo unix e foi para a tela como "1757539204", booleano que virou
/// "true" no lugar de "Sim", campo vazio descartado silenciosamente e virando "100%
/// preenchido". Cada caso abaixo é um erro que já aconteceu em produção.
/// </summary>
public class CamposDaKommoTests
{
    private static string Json(params string[] campos) => "[" + string.Join(",", campos) + "]";

    private static string Campo(long id, string nome, string? valor) =>
        $$"""{"field_id":{{id}},"field_name":"{{nome}}","value":{{(valor is null ? "null" : $"\"{valor}\"")}}}""";

    [Fact]
    public void Carimbo_unix_vira_data_legivel()
    {
        // 1757539204 = 10/09/2025 no fuso da clínica. Este número apareceu cru na ficha do
        // lead 12039 antes da correção.
        var campos = LeadService.LerCamposKommo(Json(Campo(2440907, "Data de criação lead", "1757539204")));

        var c = Assert.Single(campos);
        Assert.True(c.EhData);
        Assert.Equal("10/09/2025", c.Valor);
    }

    [Theory]
    [InlineData("3")]
    [InlineData("1")]
    [InlineData("42")]
    public void Numero_pequeno_nao_e_data(string valor)
    {
        // Sem a janela de sanidade, "3" virava 01/01/1970 e a tela mostrava data de meio
        // século atrás num campo de quantidade.
        var c = Assert.Single(LeadService.LerCamposKommo(Json(Campo(1, "Sessões previstas", valor))));

        Assert.False(c.EhData);
        Assert.Equal(valor, c.Valor);
    }

    [Theory]
    [InlineData("true", "Sim")]
    [InlineData("false", "Não")]
    [InlineData("TRUE", "Sim")]
    public void Booleano_vira_o_que_esta_escrito_no_cartao(string bruto, string esperado)
    {
        var c = Assert.Single(LeadService.LerCamposKommo(Json(Campo(2443055, "Pausar IA", bruto))));

        Assert.Equal(esperado, c.Valor);
    }

    [Fact]
    public void Campo_vazio_vem_junto_marcado_como_nao_preenchido()
    {
        // O que a SDR NÃO preencheu é metade do diagnóstico. Descartar o vazio faria o painel
        // de qualidade anunciar 100% de preenchimento numa base pela metade.
        var campos = LeadService.LerCamposKommo(Json(
            Campo(1, "Origem", "Meta-Instagram"),
            Campo(2, "Valor do tratamento", ""),
            Campo(3, "Motivo", null)));

        Assert.Equal(3, campos.Count);
        Assert.Equal(1, campos.Count(c => c.Preenchido));
        Assert.Equal(2, campos.Count(c => !c.Preenchido));
    }

    [Fact]
    public void Preenchidos_vem_antes_dos_vazios()
    {
        var campos = LeadService.LerCamposKommo(Json(
            Campo(1, "AAA vazio", ""),
            Campo(2, "ZZZ cheio", "valor")));

        Assert.Equal("ZZZ cheio", campos[0].Nome);
        Assert.False(campos[1].Preenchido);
    }

    [Fact]
    public void Json_torto_nao_derruba_a_ficha()
    {
        // Lead antigo com JSON quebrado não pode levar a página inteira junto.
        Assert.Empty(LeadService.LerCamposKommo("{isto não é json"));
        Assert.Empty(LeadService.LerCamposKommo(null));
        Assert.Empty(LeadService.LerCamposKommo(""));
    }

    [Fact]
    public void Campo_sem_nome_e_ignorado()
    {
        // Campo sem rótulo não tem como ser exibido nem conferido — vira ruído na ficha.
        var campos = LeadService.LerCamposKommo(
            """[{"field_id":1,"value":"x"},{"field_id":2,"field_name":"Origem","value":"y"}]""");

        var c = Assert.Single(campos);
        Assert.Equal("Origem", c.Nome);
    }

    [Fact]
    public void Field_id_em_texto_e_lido_igual()
    {
        // A Kommo devolve field_id ora número, ora string, dependendo do endpoint.
        var c = Assert.Single(LeadService.LerCamposKommo(
            """[{"field_id":"2440801","field_name":"Origem","value":"Indicação"}]"""));

        Assert.Equal(2440801, c.FieldId);
        Assert.Equal("Indicação", c.Valor);
    }
}
