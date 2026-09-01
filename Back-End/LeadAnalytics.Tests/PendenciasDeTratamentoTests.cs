using LeadAnalytics.Api.Service;
using Xunit;

namespace LeadAnalytics.Tests;

/// <summary>
/// O texto que a SDR lê quando é cobrada.
///
/// POR QUE ISTO MERECE TESTE
/// -------------------------
/// A cobrança nasce automática e vai para a fila de uma pessoa, todo dia, em dez
/// clínicas. Texto ruim aqui não é detalhe de estilo: cobrança que não diz o QUE
/// aconteceu vira tarefa que a SDR fecha sem entender, e cobrança que pede a coisa
/// errada — "preencher o valor" num card que só precisa ser movido — destrói a
/// confiança no aviso inteiro. Depois disso ninguém lê mais nenhum.
///
/// O marcador no começo do texto é o que impede empilhar a mesma cobrança todo dia.
/// Se ele mudar sem querer, a rotina para de reconhecer a própria tarefa e a equipe
/// recebe a mesma coisa de novo — por isso ele também está sob teste.
/// </summary>
public class PendenciasDeTratamentoTests
{
    private static PendenciaDeTratamento Pendencia(
        bool mover, bool valor, decimal? preco = 3680m) =>
        new(IdTreatment: 122106, LeadId: 13021638, Paciente: "Marlene Bezerra da Silva",
            DiaLancamento: new DateOnly(2026, 8, 14), PrecoFranquia: preco,
            PrecisaMover: mover, PrecisaValor: valor, ResponsavelId: 9876);

    /// As duas pendências juntas: o caso mais comum no mutirão.
    [Fact]
    public void Falta_mover_e_preencher_pede_as_duas_coisas()
    {
        var t = PendenciasDeTratamentoService.TextoDaTarefa(Pendencia(mover: true, valor: true));

        Assert.Equal(
            "Tratamento fechado na clínica em 14/08 (R$ 3.680) — mover para EM TRATAMENTO e preencher o valor do tratamento.",
            t);
    }

    /// Card já com valor: pede só o que falta. Pedir a coisa já feita queima o aviso.
    [Fact]
    public void Falta_so_mover_nao_pede_o_valor()
    {
        var t = PendenciasDeTratamentoService.TextoDaTarefa(Pendencia(mover: true, valor: false));

        Assert.Contains("mover para EM TRATAMENTO", t);
        Assert.DoesNotContain("preencher", t);
    }

    /// Card já na etapa: pede só o valor.
    [Fact]
    public void Falta_so_valor_nao_pede_para_mover()
    {
        var t = PendenciasDeTratamentoService.TextoDaTarefa(Pendencia(mover: false, valor: true));

        Assert.Contains("preencher o valor do tratamento", t);
        Assert.DoesNotContain("mover", t);
    }

    /// A data do lançamento é o FATO que sustenta a cobrança: sem ela a SDR não tem
    /// como saber se procede.
    [Fact]
    public void Texto_sempre_diz_quando_a_clinica_lancou()
    {
        var t = PendenciasDeTratamentoService.TextoDaTarefa(Pendencia(true, true));

        Assert.Contains("em 14/08", t);
    }

    /// Preço ausente na franquia (acontece: Doracy e José Cavalcante, agosto/2026) não
    /// vira "R$ 0" na cobrança — dizer zero sugeriria que o tratamento não valeu nada.
    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    public void Sem_preco_na_franquia_a_cobranca_nao_inventa_valor(int? preco)
    {
        var t = PendenciasDeTratamentoService.TextoDaTarefa(
            Pendencia(true, true, preco is null ? null : preco.Value));

        Assert.DoesNotContain("R$", t);
        Assert.Contains("mover para EM TRATAMENTO", t);
    }

    /// Valor grande sai legível em português (ponto de milhar), não "R$ 12,000".
    [Fact]
    public void Valor_sai_no_formato_brasileiro()
    {
        var t = PendenciasDeTratamentoService.TextoDaTarefa(Pendencia(true, false, 12000m));

        Assert.Contains("R$ 12.000", t);
    }

    /// O marcador abre o texto: é por ele que a rotina reconhece a própria cobrança e
    /// não cria a mesma tarefa amanhã.
    [Fact]
    public void Texto_comeca_pelo_marcador_que_evita_duplicar()
    {
        var t = PendenciasDeTratamentoService.TextoDaTarefa(Pendencia(true, true));

        Assert.StartsWith(PendenciasDeTratamentoService.Marcador, t);
    }
}
