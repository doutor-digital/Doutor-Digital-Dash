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
