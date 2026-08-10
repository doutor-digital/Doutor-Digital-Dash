using LeadAnalytics.Api.Service.Spine;
using Xunit;

namespace LeadAnalytics.Tests;

/// <summary>
/// O erro caro aqui não é deixar de escrever: é escrever no cartão errado. Marcar
/// falta em quem compareceu faz o SDR ligar cobrando presença de quem esteve na
/// clínica. Estes testes guardam as regras que evitam isso.
/// </summary>
public class ConsultaSituacaoTests
{
    [Theory]
    [InlineData(SpineApiClient.ScheduleStatus.Atendido, 1840177L)]
    [InlineData(SpineApiClient.ScheduleStatus.NaoCompareceu, 1840179L)]
    [InlineData(SpineApiClient.ScheduleStatus.Remarcado, 1840181L)]
    [InlineData(SpineApiClient.ScheduleStatus.Desmarcado, 1840183L)]
    [InlineData(SpineApiClient.ScheduleStatus.Agendado, 1840173L)]
    [InlineData(SpineApiClient.ScheduleStatus.Confirmado, 1840175L)]
    public void Cada_situacao_da_franquia_cai_na_opcao_certa(int spine, long enumId)
    {
        Assert.Equal(enumId, ConsultaSituacaoSyncService.EnumSituacao(spine));
    }

    [Fact]
    public void Situacao_que_a_franquia_criar_depois_nao_vira_palpite()
    {
        // A franquia pode adicionar uma situação nova sem avisar. Melhor o campo
        // vazio do que o cartão afirmando um desfecho que ninguém registrou.
        Assert.Null(ConsultaSituacaoSyncService.EnumSituacao(999));
    }

    [Theory]
    [InlineData(SpineApiClient.ScheduleCategory.Avaliacao, 1840185L)]
    [InlineData(SpineApiClient.ScheduleCategory.Sessao, 1840187L)]
    [InlineData(SpineApiClient.ScheduleCategory.Retorno, 1840189L)]
    [InlineData(SpineApiClient.ScheduleCategory.RetornoComExames, 1840191L)]
    [InlineData(SpineApiClient.ScheduleCategory.RetornoAposTratamento, 1840193L)]
    public void Cada_categoria_da_franquia_cai_na_opcao_certa(int spine, long enumId)
    {
        Assert.Equal(enumId, ConsultaSituacaoSyncService.EnumCategoria(spine));
    }

    // ─── O que atravessa e o que não ────────────────────────────────────────

    [Fact]
    public void So_desfecho_volta_para_a_Kommo()
    {
        // Agendado e confirmado a Kommo já sabe: foi ela quem marcou. Reescrever
        // isso só gastaria chamada e sujaria o histórico do cartão.
        Assert.True(ConsultaSituacaoSyncService.EhDesfecho(SpineApiClient.ScheduleStatus.Atendido));
        Assert.True(ConsultaSituacaoSyncService.EhDesfecho(SpineApiClient.ScheduleStatus.NaoCompareceu));
        Assert.True(ConsultaSituacaoSyncService.EhDesfecho(SpineApiClient.ScheduleStatus.Remarcado));
        Assert.True(ConsultaSituacaoSyncService.EhDesfecho(SpineApiClient.ScheduleStatus.Desmarcado));

        Assert.False(ConsultaSituacaoSyncService.EhDesfecho(SpineApiClient.ScheduleStatus.Agendado));
        Assert.False(ConsultaSituacaoSyncService.EhDesfecho(SpineApiClient.ScheduleStatus.Confirmado));
    }

    // ─── Casamento por nome ─────────────────────────────────────────────────

    [Fact]
    public void Nome_casa_apesar_de_caixa_e_espaco_sobrando()
    {
        // A agenda da franquia grava em caixa alta; a Kommo, como a recepção digitou.
        Assert.Equal(
            ConsultaSituacaoSyncService.ChaveNome("MARIA  DA   SILVA"),
            ConsultaSituacaoSyncService.ChaveNome("  Maria da Silva "));
    }

    [Fact]
    public void Nome_com_acento_diferente_nao_casa()
    {
        // "Vera" e "Verá" são pessoas diferentes com mais frequência do que são a
        // mesma mal digitada. Ignorar acento aqui aumentaria o falso positivo no
        // pior lugar possível: escrever falta no cartão de outra pessoa.
        Assert.NotEqual(
            ConsultaSituacaoSyncService.ChaveNome("Vera Lucia"),
            ConsultaSituacaoSyncService.ChaveNome("Verá Lucia"));
    }

    [Fact]
    public void Nome_vazio_nao_vira_chave_que_casa_com_tudo()
    {
        // Um lead sem nome na Kommo não pode virar o destino de qualquer consulta
        // cujo paciente também esteja em branco.
        Assert.Equal(string.Empty, ConsultaSituacaoSyncService.ChaveNome(null));
        Assert.Equal(string.Empty, ConsultaSituacaoSyncService.ChaveNome("   "));
    }
}
