namespace LeadAnalytics.Api.DTOs.Auth;

public class LoginRequestDto
{
    public string? Name { get; set; }
    public string? Email { get; set; }
    public string? Password { get; set; }
}
