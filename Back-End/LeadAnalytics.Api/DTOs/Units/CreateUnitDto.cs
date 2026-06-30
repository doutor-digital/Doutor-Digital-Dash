using System.ComponentModel.DataAnnotations;

namespace LeadAnalytics.Api.DTOs.Units;

/// <summary>Payload para criar uma nova unidade (botão "+" na tela de unidades).</summary>
public class CreateUnitDto
{
    [Required(ErrorMessage = "O nome da unidade é obrigatório.")]
    [StringLength(120, MinimumLength = 2)]
    public string Name { get; set; } = null!;

    /// <summary>Segmento de negócio: "saude" (padrão) ou "juridico". Define o conjunto de KPIs do dashboard.</summary>
    [StringLength(32)]
    public string? Segment { get; set; }

    [EmailAddress(ErrorMessage = "E-mail inválido.")]
    public string? Email { get; set; }

    [StringLength(18)]
    public string? Cnpj { get; set; }

    public string? Phone { get; set; }
    public string? AddressLine { get; set; }
    public string? AddressNumber { get; set; }
    public string? Neighborhood { get; set; }
    public string? City { get; set; }

    [StringLength(2, MinimumLength = 2, ErrorMessage = "UF deve ter 2 letras.")]
    public string? State { get; set; }

    [StringLength(10)]
    public string? ZipCode { get; set; }

    public string? PhotoUrl { get; set; }
    public string? ResponsibleName { get; set; }

    /// <summary>Subdomínio da conta Kommo (ex.: "minhaclinica").</summary>
    public string? KommoSubdomain { get; set; }

    /// <summary>account_id da conta Kommo (opcional; ajuda a validar a origem do webhook).</summary>
    public string? KommoAccountId { get; set; }

    /// <summary>
    /// Slug desejado para a URL do webhook. Se vazio, é gerado a partir do nome.
    /// </summary>
    public string? Slug { get; set; }

    /// <summary>
    /// TenantId (ClinicId) da unidade. Se não informado, a API gera o próximo disponível.
    /// </summary>
    public int? ClinicId { get; set; }
}
