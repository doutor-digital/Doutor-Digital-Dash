namespace LeadAnalytics.Api.DTOs.Cloudia;

/// <summary>
/// ⚠️ ATENÇÃO: Dados da Cloudia NÃO são confiáveis
/// Usar apenas como FALLBACK quando não houver OriginEvent da Meta
/// </summary>
public class CloudiaAdDataDto
{
    public string? Source { get; set; }
    public string? AdId { get; set; }
    public string? AdName { get; set; }
}