using System.ComponentModel.DataAnnotations;

namespace LeadAnalytics.Api.DTOs.Auth;

public class VerifyResetCodeRequestDto
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    [StringLength(6, MinimumLength = 6)]
    public string Code { get; set; } = string.Empty;
}
