using LeadAnalytics.Api.Service;
using Xunit;

namespace LeadAnalytics.Tests;

/// <summary>
/// Rótulo e cor da jornada do lead.
///
/// Etapa apagada na Kommo vira id numérico cru no nosso histórico — em Imperatriz são mais de
/// 1 700 linhas assim. Mostrar "106037703" na linha do tempo faz a tela parecer defeito; e
/// pintar de verde uma etapa de perda faz alguém ler o oposto do que aconteceu.
/// </summary>
public class JornadaTests
{
    [Theory]
    [InlineData("106037703", "Etapa removida (106037703)")]
    [InlineData("105811055", "Etapa removida (105811055)")]
    public void Id_numerico_cru_vira_texto_que_explica(string etapa, string esperado)
    {
        Assert.Equal(esperado, JornadaService.Rotulo(etapa));
    }

    [Theory]
    [InlineData("TRATAMENTO_CANCELADO", "TRATAMENTO CANCELADO")]
    [InlineData("04_AGENDADO_SEM_PAGAMENTO", "04 AGENDADO SEM PAGAMENTO")]
    public void Etapa_com_nome_so_perde_o_sublinhado(string etapa, string esperado)
    {
        Assert.Equal(esperado, JornadaService.Rotulo(etapa));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Etapa_ausente_vira_travessao(string? etapa)
    {
        Assert.Equal("—", JornadaService.Rotulo(etapa));
    }

    [Fact]
    public void Numero_curto_nao_e_confundido_com_id_de_etapa()
    {
        // Só a partir de 6 dígitos é id de status da Kommo. "12345" continua sendo o rótulo.
        Assert.Equal("12345", JornadaService.Rotulo("12345"));
    }

    [Theory]
    [InlineData("PERDIDO", "ruim")]
    [InlineData("08_NAO_FECHOU_TRATAMENTO", "ruim")]
    [InlineData("TRATAMENTO_CANCELADO", "ruim")]
    [InlineData("DESCARTADO", "ruim")]
    [InlineData("10_EM_TRATAMENTO", "ok")]
    [InlineData("04_AGENDADO_SEM_PAGAMENTO", "atencao")]
    [InlineData("QUALIFICACAO", "neutro")]
    public void A_cor_da_etapa_diz_o_que_aconteceu(string etapa, string tomEsperado)
    {
        Assert.Equal(tomEsperado, AtividadeService.TomDaEtapa(etapa));
    }

    [Theory]
    [InlineData("Arlan Souza da Silva", "Arlan")]
    [InlineData("Maria", "Maria")]
    [InlineData("  João Batista  ", "João")]
    [InlineData("", "Sem nome")]
    [InlineData(null, "Sem nome")]
    public void O_log_usa_so_o_primeiro_nome(string? nome, string esperado)
    {
        // A linha do log tem largura de terminal: nome inteiro empurra o resto para fora.
        Assert.Equal(esperado, AtividadeService.PrimeiroNome(nome));
    }
}
