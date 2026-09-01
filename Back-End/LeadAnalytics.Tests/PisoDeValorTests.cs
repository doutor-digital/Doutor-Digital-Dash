using LeadAnalytics.Api.Controllers;
using Xunit;

namespace LeadAnalytics.Tests;

/// <summary>
/// A trava que decide o que a gente escreve no CRM do cliente.
///
/// POR QUE ESTE ARQUIVO EXISTE
/// ---------------------------
/// Esta é a única rotina do sistema que ESCREVE num CRM de produção. Todo o resto
/// mostra número errado; esta grava número errado, e um valor gravado ninguém mais
/// desconfia — ele vira receita, vira meta, vira conversa com o franqueado.
///
/// O caso real que a criou (01/09/2026): a franquia devolvia R$ 368 e R$ 420 em
/// Parauapebas, exatamente um décimo dos R$ 3.680 e R$ 4.200 que a unidade pratica.
/// Preencher o campo vazio com isso seria trocar "não sei" por "sei errado".
/// </summary>
public class PisoDeValorTests
{
    private static LinhaReconciliacao Linha(
        long id, long? leadId, decimal? preco, string? valorKommo = null) =>
        new(id, leadId, $"Paciente {id}", "…91234567", preco, leadId is null ? null : "Lead", valorKommo, leadId is not null);

    private static readonly decimal Piso = SelecaoDeEscrita.PisoPadrao;

    /// Preço normal, campo vazio, lead casado: grava.
    [Fact]
    public void Preco_normal_com_campo_vazio_e_gravado()
    {
        var (gravar, suspeitos) = SelecaoDeEscrita.Separar(new[] { Linha(1, 10, 3680m) }, Piso);

        Assert.Equal(1, gravar.Count);
        Assert.Empty(suspeitos);
    }

    /// O caso de Parauapebas: um décimo do valor real não entra no CRM.
    [Theory]
    [InlineData(368)]
    [InlineData(420)]
    [InlineData(999)]
    public void Preco_com_digito_faltando_nao_e_gravado(int preco)
    {
        var (gravar, suspeitos) = SelecaoDeEscrita.Separar(new[] { Linha(1, 10, preco) }, Piso);

        Assert.Empty(gravar);
        Assert.Equal(1, suspeitos.Count);
    }

    /// R$ 1.600 é o menor preço legítimo medido na rede (Boa Vista) — tem que passar.
    /// Se o piso subir e engolir esse valor, o teste cai antes da produção.
    [Fact]
    public void Menor_preco_legitimo_da_rede_passa()
    {
        var (gravar, _) = SelecaoDeEscrita.Separar(new[] { Linha(1, 10, 1600m) }, Piso);

        Assert.Equal(1, gravar.Count);
    }

    /// Exatamente no piso passa: a trava é "abaixo de", não "até".
    [Fact]
    public void Valor_exatamente_no_piso_passa()
    {
        var (gravar, suspeitos) = SelecaoDeEscrita.Separar(new[] { Linha(1, 10, Piso) }, Piso);

        Assert.Equal(1, gravar.Count);
        Assert.Empty(suspeitos);
    }

    /// Campo já preenchido nunca é tocado, nem quando o valor da franquia diverge:
    /// a rota completa vazio, não corrige o que um humano digitou.
    [Fact]
    public void Campo_ja_preenchido_nao_e_tocado()
    {
        var (gravar, suspeitos) = SelecaoDeEscrita.Separar(
            new[] { Linha(1, 10, 3680m, valorKommo: "4200") }, Piso);

        Assert.Empty(gravar);
        Assert.Empty(suspeitos);
    }

    /// Sem lead casado não há onde gravar — e não é suspeita, é ausência.
    [Fact]
    public void Tratamento_sem_lead_na_kommo_fica_de_fora()
    {
        var (gravar, suspeitos) = SelecaoDeEscrita.Separar(new[] { Linha(1, null, 3680m) }, Piso);

        Assert.Empty(gravar);
        Assert.Empty(suspeitos);
    }

    /// Preço ausente ou zero na franquia (Açailândia, Balsas e Serra em ago/2026)
    /// não vira R$ 0 na Kommo: zero diria "vendeu de graça".
    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    public void Sem_preco_na_franquia_nao_grava_zero(int? preco)
    {
        var (gravar, suspeitos) = SelecaoDeEscrita.Separar(
            new[] { Linha(1, 10, preco is null ? null : preco.Value) }, Piso);

        Assert.Empty(gravar);
        Assert.Empty(suspeitos);
    }

    /// Piso 0 desliga a trava, para o dia em que a rede vender algo barato de verdade.
    [Fact]
    public void Piso_zero_desliga_a_trava()
    {
        var (gravar, suspeitos) = SelecaoDeEscrita.Separar(new[] { Linha(1, 10, 368m) }, 0m);

        Assert.Equal(1, gravar.Count);
        Assert.Empty(suspeitos);
    }

    /// Um lote misto sai separado corretamente — é assim que a rota o consome.
    [Fact]
    public void Lote_misto_separa_cada_linha_no_seu_lugar()
    {
        var (gravar, suspeitos) = SelecaoDeEscrita.Separar(new[]
        {
            Linha(1, 10, 3680m),                      // grava
            Linha(2, 11, 368m),                       // suspeito
            Linha(3, null, 3680m),                    // sem lead
            Linha(4, 12, 4200m, valorKommo: "4200"),  // já preenchido
            Linha(5, 13, 420m),                       // suspeito
            Linha(6, 14, 1600m),                      // grava
        }, Piso);

        Assert.Equal(new long[] { 1, 6 }, gravar.Select(l => l.IdTreatment).ToArray());
        Assert.Equal(new long[] { 2, 5 }, suspeitos.Select(l => l.IdTreatment).ToArray());
    }
}
