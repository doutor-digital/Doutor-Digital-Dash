using System.ComponentModel.DataAnnotations;

namespace LeadAnalytics.Api.DTOs.User;

public class UpdateUserDto
{
    [MaxLength(120)]
    public string? Name { get; set; }

    [EmailAddress]
    [MaxLength(180)]
    public string? Email { get; set; }

    [MinLength(8)]
    public string? Password { get; set; }

    [MaxLength(30)]
    public string? Role { get; set; }
}
