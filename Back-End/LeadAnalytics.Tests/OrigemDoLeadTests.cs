using LeadAnalytics.Api.Service;
using Xunit;

namespace LeadAnalytics.Tests;

/// <summary>
/// De onde o lead veio.
///
/// POR QUE ESTE ARQUIVO EXISTE
/// ---------------------------
/// A tela de leads recentes mostrava "Top origens: Kommo 15", e o filtro de origem do
/// dashboard oferecia uma opção só. Ninguém tinha errado uma conta: o código lia a coluna
/// leads.Source, que guarda o canal de entrada no nosso sistema e vale "Kommo" em 100% dos
/// leads. Era a pergunta certa respondida com o dado de outra.
///
/// O defeito sobreviveu meses porque "Kommo 15" é plausível numa tela de CRM. O que trava
/// a volta dele é o primeiro teste daqui.
///
/// Os nomes de campo e os valores sao os reais da Kommo, conferidos em producao
/// em 02/09/2026.
/// </summary>
public class OrigemDoLeadTests
{
    private static string Cartao(params (string campo, string valor)[] campos) =>
        "[" + string.Join(",", campos.Select(c =>
            $$"""{"field_name":"{{c.campo}}","value":"{{c.valor}}"}""")) + "]";

    // ─── O caso que motivou tudo ────────────────────────────────────────────────

    /// A origem sai do cartao. Quem chama decide o que fazer quando ela nao existe --
    /// mas nao pode receber "Kommo" achando que recebeu uma origem.
    [Fact]
    public void Le_a_origem_do_cartao_e_nao_o_canal_de_entrada()
    {
        var origem = OrigemDoLead.Ler(Cartao(("⚑ Origem", "Meta-Instagram")));
        Assert.Equal("Meta-Instagram", origem);
    }

    [Theory]
    [InlineData("Meta-Instagram")]
    [InlineData("Meta-Facebook")]
    [InlineData("Org-Facebook")]
    [InlineData("Indicação")]
    [InlineData("Site oficial - Franquia")]
    [InlineData("Fachada")]
    [InlineData("Sem origem")]
    public void Devolve_o_rotulo_exato_que_a_clinica_cadastrou(string valor)
    {
        Assert.Equal(valor, OrigemDoLead.Ler(Cartao(("⚑ Origem", valor))));
    }

    /// Base antiga grava o campo sem o simbolo de grupo.
    [Fact]
    public void Aceita_o_nome_antigo_sem_simbolo()
    {
        Assert.Equal("Google", OrigemDoLead.Ler(Cartao(("Origem", "Google"))));
    }

    // ─── Os campos que parecem, mas nao sao ─────────────────────────────────────

    /// <summary>
    /// Tres campos da Kommo terminam em "origem" e nenhum deles e a origem do lead.
    /// "Canal de origem" e lista editavel pela clinica, entao um dia pode ganhar um
    /// valor que passe por qualquer heuristica de conteudo -- a defesa tem de estar
    /// no nome do campo.
    /// </summary>
    [Theory]
    [InlineData("Canal de origem", "Instagram")]
    [InlineData("⌂ Plataforma de origem", "facebook")]
    [InlineData("⌂ URL de origem do clique", "https://fb.me/6eplrKqaJ")]
    public void Campos_que_terminam_em_origem_mas_nao_sao_a_origem(string campo, string valor)
    {
        Assert.Null(OrigemDoLead.Ler(Cartao((campo, valor))));
    }

    /// Com os impostores no mesmo cartao, ainda tem de achar o certo.
    [Fact]
    public void Escolhe_o_campo_certo_no_meio_dos_parecidos()
    {
        var cartao = Cartao(
            ("Canal de origem", "Instagram"),
            ("⌂ Plataforma de origem", "facebook"),
            ("⚑ Origem", "Meta-Facebook"),
            ("⌂ URL de origem do clique", "https://fb.me/x"));

        Assert.Equal("Meta-Facebook", OrigemDoLead.Ler(cartao));
    }

    // ─── Ausencia e sujeira ─────────────────────────────────────────────────────

    [Fact]
    public void Cartao_sem_o_campo_devolve_nulo()
    {
        Assert.Null(OrigemDoLead.Ler(Cartao(("⌂ ID do anúncio", "120248194602320178"))));
    }

    /// Campo presente e vazio e o mesmo que ausente: nao inventa "" como origem.
    [Fact]
    public void Campo_vazio_conta_como_ausente()
    {
        Assert.Null(OrigemDoLead.Ler(Cartao(("⚑ Origem", ""))));
        Assert.Null(OrigemDoLead.Ler(Cartao(("⚑ Origem", "   "))));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{ isto nao e json")]
    [InlineData("{\"nao\":\"e um array\"}")]
    public void Entrada_torta_devolve_nulo_em_vez_de_explodir(string? json)
    {
        Assert.Null(OrigemDoLead.Ler(json));
    }

    // ─── O reconhecedor do nome do campo ────────────────────────────────────────

    /// <summary>
    /// EhCampoOrigem e compartilhado com o painel de cobertura do rastreio, e a versao SQL
    /// dele vive no btrim de GetDistinctSourcesAsync. Os tres precisam concordar sobre
    /// quais simbolos a Kommo usa como prefixo de grupo.
    /// </summary>
    [Theory]
    [InlineData("⚑ origem", true)]
    [InlineData("⌂ origem", true)]
    [InlineData("☎ origem", true)]
    [InlineData("origem", true)]
    [InlineData("  origem", true)]
    [InlineData("canal de origem", false)]
    [InlineData("⌂ plataforma de origem", false)]
    [InlineData("⌂ url de origem do clique", false)]
    [InlineData("origem do lead", false)]
    public void Reconhece_o_campo_de_origem(string nomeMinusculo, bool esperado)
    {
        Assert.Equal(esperado, OrigemDoLead.EhCampoOrigem(nomeMinusculo));
    }
}
