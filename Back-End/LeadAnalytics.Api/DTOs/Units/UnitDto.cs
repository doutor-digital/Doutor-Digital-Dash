namespace LeadAnalytics.Api.DTOs.Units;

/// <summary>
/// Representação de uma unidade para o front-end. Inclui a URL completa do webhook
/// da Kommo (pronta para colar em Configurações → Integrações → Web hooks) e a
/// contagem de leads já recebidos por aquela unidade.
/// </summary>
public class UnitDto
{
    public int Id { get; set; }
    public int ClinicId { get; set; }
    public string Name { get; set; } = null!;
    public string? Slug { get; set; }

    public string? Email { get; set; }
    public string? Cnpj { get; set; }
    public string? Phone { get; set; }
    public string? AddressLine { get; set; }
    public string? AddressNumber { get; set; }
    public string? Neighborhood { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? ZipCode { get; set; }
    public string? PhotoUrl { get; set; }
    public string? ResponsibleName { get; set; }
    public bool IsActive { get; set; }

    public string? KommoSubdomain { get; set; }
    public string? KommoAccountId { get; set; }
    public string? KommoStageMapJson { get; set; }

    /// <summary>URL completa do webhook desta unidade — cole na Kommo. Pode ser null se faltar slug.</summary>
    public string? WebhookUrl { get; set; }

    public int LeadCount { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
