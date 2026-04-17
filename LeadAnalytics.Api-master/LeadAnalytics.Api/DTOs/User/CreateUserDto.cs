using System.ComponentModel.DataAnnotations;

namespace LeadAnalytics.Api.DTOs.User;

public class CreateUserDto
{
    [Required]
    [MaxLength(120)]
    public string Name { get; set; } = null!;

    [Required]
    [EmailAddress]
    [MaxLength(180)]
    public string Email { get; set; } = null!;

    [Required]
    [MinLength(8)]
    public string Password { get; set; } = null!;

    [MaxLength(30)]
    public string? Role { get; set; }
}
