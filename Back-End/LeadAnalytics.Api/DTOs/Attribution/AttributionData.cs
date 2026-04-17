namespace LeadAnalytics.Api.DTOs.Attribution;

/// <summary>
/// Dados extraídos de um OriginEvent para atribuição
/// </summary>
public record AttributionData
{
    public string Source { get; init; } = "DESCONHECIDO";
    public string Campaign { get; init; } = "DESCONHECIDO";
    public string? Ad { get; init; }
    public string Confidence { get; init; } = "BAIXA";
}