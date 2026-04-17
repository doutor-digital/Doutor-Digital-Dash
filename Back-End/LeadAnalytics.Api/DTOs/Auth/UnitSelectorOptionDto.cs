using LeadAnalytics.Api.Models;

namespace LeadAnalytics.Api.DTOs.Auth;

public class UnitSelectorOptionDto
{
    public int Id { get; set; }
    public int ClinicId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string LogoUrl { get; set; } = string.Empty;
    public bool IsDefault { get; set; }

    public static implicit operator UnitSelectorOptionDto(Unit v)
    {
        throw new NotImplementedException();
    }
}
