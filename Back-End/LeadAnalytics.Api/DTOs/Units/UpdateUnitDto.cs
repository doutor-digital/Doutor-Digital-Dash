using System.ComponentModel.DataAnnotations;

namespace LeadAnalytics.Api.DTOs.Units;

/// <summary>
/// Atualização parcial de uma unidade. Campos nulos são ignorados (não sobrescrevem).
/// O <c>ClinicId</c> (TenantId) e o <c>Slug</c> não são alteráveis após a criação
/// para não quebrar a URL do webhook já cadastrada na Kommo.
/// </summary>
public class UpdateUnitDto
{
    [StringLength(120, MinimumLength = 2)]
    public string? Name { get; set; }

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

    public string? KommoSubdomain { get; set; }
    public string? KommoAccountId { get; set; }

    /// <summary>
    /// Mapa JSON status_id da Kommo → etapa canônica. Ex.:
    /// {"67548619":"AGENDADO_COM_PAGAMENTO"}. Etapas válidas: ENTRADA_LEAD,
    /// AGENDADO_SEM_PAGAMENTO, AGENDADO_COM_PAGAMENTO, NAO_COMPARECEU,
    /// COMPARECEU_CONSULTA, TRATAMENTO_FECHADO, NAO_DEU_CONTINUIDADE.
    /// </summary>
    public string? KommoStageMapJson { get; set; }

    public bool? IsActive { get; set; }
}
