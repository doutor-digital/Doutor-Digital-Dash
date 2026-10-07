using LeadAnalytics.Api.Service;
using Xunit;

namespace LeadAnalytics.Tests;

/// <summary>
/// Regra única da receita (07/10/2026, João): franquia primeiro, Kommo se faltar — por paciente.
/// </summary>
public class ReceitaFranquiaPrimeiroTests
{
    private static KpiConfigService.ReceitaSomada Soma(params (long? lead, decimal? franquia, decimal? kommo)[] l)
        => KpiConfigService.SomarReceita(l.Select(x => (x.lead, x.franquia, x.kommo)));

    [Fact]
    public void Preco_da_franquia_vale_mesmo_com_valor_errado_no_Kommo()
        => Assert.Equal(3680m, Soma((1, 3680m, 368m)).Total);

    [Fact]
    public void Franquia_zerada_ou_vazia_usa_o_valor_do_cartao()
    {
        Assert.Equal(4200m, Soma((1, 0m, 4200m)).Total);
        Assert.Equal(3500m, Soma((1, null, 3500m)).Total);
    }

    [Fact]
    public void Dois_tratamentos_no_mesmo_cartao_nao_somam_o_cartao_duas_vezes()
    {
        // o cruzamento copia o ¤ Valor do tratamento (um por cartão) em cada tratamento do lead
        var r = Soma((7, 0m, 3680m), (7, 0m, 3680m));
        Assert.Equal(3680m, r.Total);
        Assert.Equal(2, r.ComValor);
    }

    [Fact]
    public void Se_a_franquia_precificou_algum_tratamento_do_paciente_vale_a_franquia()
    {
        var r = Soma((7, 3680m, 5530m), (7, 0m, 5530m));
        Assert.Equal(3680m, r.Total);
    }

    [Fact]
    public void Tratamento_sem_lead_casado_so_tem_a_franquia()
    {
        var r = Soma((null, 1850m, null), (null, 0m, null));
        Assert.Equal(1850m, r.Total);
        Assert.Equal(1, r.ComValor);
        Assert.Equal(1, r.SemValor);
    }

    [Fact]
    public void Sem_valor_nos_dois_lados_fica_sem_valor()
    {
        var r = Soma((1, 0m, null), (2, null, null), (3, 0m, 0m));
        Assert.Equal(0m, r.Total);
        Assert.Equal(3, r.SemValor);
        Assert.Equal(3, r.Tratamentos);
    }

    [Fact]
    public void Estorno_negativo_da_franquia_conta_como_valor()
        => Assert.Equal(-3680m, Soma((1, -3680m, 3680m)).Total);
}
