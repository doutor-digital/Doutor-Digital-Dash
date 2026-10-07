using LeadAnalytics.Api.Service;
using Xunit;

namespace LeadAnalytics.Tests;

/// <summary>
/// Regra única da receita (07/10/2026, João): franquia primeiro, Kommo se faltar.
/// </summary>
public class ReceitaFranquiaPrimeiroTests
{
    [Fact]
    public void Preco_da_franquia_vale_mesmo_com_valor_no_Kommo()
        => Assert.Equal(3680m, KpiConfigService.ValorDoTratamento(3680m, 368m));

    [Fact]
    public void Franquia_zerada_usa_o_valor_do_Kommo()
        => Assert.Equal(4200m, KpiConfigService.ValorDoTratamento(0m, 4200m));

    [Fact]
    public void Franquia_vazia_usa_o_valor_do_Kommo()
        => Assert.Equal(3500m, KpiConfigService.ValorDoTratamento(null, 3500m));

    [Fact]
    public void Sem_valor_nos_dois_lados_conta_zero()
    {
        Assert.Equal(0m, KpiConfigService.ValorDoTratamento(0m, null));
        Assert.Equal(0m, KpiConfigService.ValorDoTratamento(null, null));
        Assert.Equal(0m, KpiConfigService.ValorDoTratamento(0m, 0m));
    }
}
