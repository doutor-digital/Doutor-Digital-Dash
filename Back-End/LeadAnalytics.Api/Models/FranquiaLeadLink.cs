namespace LeadAnalytics.Api.Models;

/// <summary>
/// O vínculo entre um tratamento da FRANQUIA e o lead da KOMMO, casado por telefone.
///
/// POR QUE ISTO É GRAVADO E NÃO CALCULADO NA HORA
/// ----------------------------------------------
/// Cada cruzamento custa duas chamadas por tratamento — a ficha do paciente na franquia
/// (que tem o telefone) e a busca do lead na Kommo. Vinte e dois tratamentos viram
/// quarenta e quatro requisições, e a Kommo limita. Fazer isso a cada carregamento de
/// dashboard derruba a tela; fazer uma vez por dia resolve.
///
/// A POPULAÇÃO É DA FRANQUIA; O VALOR, FRANQUIA PRIMEIRO E KOMMO SE FALTAR
/// --------------------------------------------------------------------------
/// Conta-se o que a clínica lançou no sistema dela (por isso a linha existe por tratamento,
/// não por lead). O dinheiro, desde 07/10/2026, é o preço lançado na franquia; onde ela não
/// lançou, o ¤ Valor do tratamento do cartão — decidido por paciente, porque o valor do cartão
/// é um só e fica copiado em cada tratamento do lead (KpiConfigService.SomarReceita). Guardar
/// os dois lados permite auditar a divergência sem refazer o cruzamento.
/// </summary>
public class FranquiaLeadLink
{
    public int Id { get; set; }
    public int UnitId { get; set; }

    /// <summary>Id do tratamento na franquia — a chave do vínculo.</summary>
    public long IdTreatment { get; set; }

    /// <summary>Dia LOCAL do lançamento. É por ele que o período recorta.</summary>
    public DateOnly DiaLancamento { get; set; }

    public string? Paciente { get; set; }

    /// <summary>Últimos 8 dígitos — o que casa com a Kommo. Não guarda o número inteiro.</summary>
    public string? Telefone { get; set; }

    public decimal? PrecoFranquia { get; set; }
    public long? LeadId { get; set; }

    /// <summary>Valor do campo customizado na Kommo no momento do cruzamento.</summary>
    public decimal? ValorKommo { get; set; }

    public DateTime AtualizadoEm { get; set; }
}
