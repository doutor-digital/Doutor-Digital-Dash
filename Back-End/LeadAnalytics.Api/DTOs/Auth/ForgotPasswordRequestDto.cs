using System.ComponentModel.DataAnnotations;

namespace LeadAnalytics.Api.DTOs.Auth;

public class ForgotPasswordRequestDto
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;
}
