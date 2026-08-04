using System.Text.Json.Serialization;

namespace LeadAnalytics.Api.DTOs.Response;

/// <summary>
/// Leads que entraram no fim de semana, dentro do período do dashboard.
///
/// Sábado e domingo são contados pelo relógio COMERCIAL (o dia da clínica vai das
/// 19h às 19h), então um lead que chega sexta às 22h já conta como sábado — igual
/// ao resto das métricas por dia. É o público que ninguém atendeu na hora e que
/// precisa de retomada na segunda; por isso o card mostra também de onde vieram.
/// </summary>
public class FimDeSemanaDto
{
    /// <summary>Total de leads de sábado + domingo no período.</summary>
    public int Total { get; set; }

    public int Sabado { get; set; }
    public int Domingo { get; set; }

    /// <summary>Distribuição por origem, da maior para a menor.</summary>
    public List<OrigemAgrupadaDto> Origens { get; set; } = new();
}

/// <summary>
/// Dinheiro do período: quanto de tratamento foi fechado e o ticket médio.
///
/// Só entram leads com valor preenchido — por isso <see cref="ComValor"/> vem junto:
/// um ticket médio calculado sobre 3 de 200 fechamentos é ruído, e o card precisa
/// deixar isso visível em vez de exibir um número bonito e falso.
/// </summary>
public class ReceitaResumoDto
{
    [System.Text.Json.Serialization.JsonPropertyName("receita_fechada")]
    public decimal ReceitaFechada { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("ticket_medio")]
    public decimal TicketMedio { get; set; }

    /// <summary>Quantos leads fechados tinham valor preenchido (base do ticket médio).</summary>
    [System.Text.Json.Serialization.JsonPropertyName("com_valor")]
    public int ComValor { get; set; }

    /// <summary>Total de leads que fecharam tratamento no período (com ou sem valor).</summary>
    public int Fechados { get; set; }
}

/// <summary>Um motivo de não-conversão e quantas vezes apareceu no período.</summary>
public class MotivoPerdaDto
{
    public string Motivo { get; set; } = "";
    public int Quantidade { get; set; }
}

/// <summary>
/// Funil condensado por origem: de cada mídia, quantos leads chegaram a agendar e
/// quantos fecharam tratamento.
///
/// Volume de lead por origem engana — a mídia que traz mais lead costuma não ser a
/// que traz mais paciente. Este recorte é o que separa custo de aquisição barato de
/// aquisição eficaz. A origem vem do custom field da Kommo, não de
/// <c>Lead.Source</c> (essa coluna guarda o sistema de origem do dado e é constante).
/// </summary>
public class FunilOrigemDto
{
    public string Origem { get; set; } = "";

    /// <summary>Leads da origem no período.</summary>
    public int Total { get; set; }

    /// <summary>Quantos chegaram a ter consulta agendada (ou passaram disso).</summary>
    public int Agendados { get; set; }

    /// <summary>Quantos fecharam tratamento.</summary>
    public int Fechados { get; set; }
}

/// <summary>
/// Desempenho de um anúncio: quantos leads ele trouxe e quantos deles agendaram.
///
/// Volume de lead por anúncio não decide verba — anúncio que traz muito lead e
/// nenhum agendamento custa caro. Por isso o card carrega as duas colunas juntas.
/// A origem vem do campo que o rastreio de campanha grava no cartão (CTWA), não
/// de <c>Lead.Source</c>.
/// </summary>
public class AnuncioDesempenhoDto
{
    /// <summary>Título do anúncio quando disponível; senão o id.</summary>
    public string Anuncio { get; set; } = "";

    /// <summary>Leads que chegaram por este anúncio no período.</summary>
    public int Total { get; set; }

    /// <summary>Destes, quantos chegaram a agendar consulta.</summary>
    public int Agendados { get; set; }

    /// <summary>Nome do anúncio na Meta, quando a conta de anúncios está conectada.</summary>
    public string? Nome { get; set; }

    /// <summary>Miniatura do criativo (CDN da Meta). Nulo = card mostra só o texto.</summary>
    public string? Thumbnail { get; set; }

    /// <summary>Link para a peça no Facebook/Instagram, quando a Meta devolve.</summary>
    public string? Permalink { get; set; }
}

/// <summary>Um tipo de tratamento indicado e quantas vezes foi indicado no período.</summary>
public class TratamentoIndicadoDto
{
    public string Tratamento { get; set; } = "";
    public int Quantidade { get; set; }
}
