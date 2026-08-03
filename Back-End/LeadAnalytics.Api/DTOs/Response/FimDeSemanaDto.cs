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
