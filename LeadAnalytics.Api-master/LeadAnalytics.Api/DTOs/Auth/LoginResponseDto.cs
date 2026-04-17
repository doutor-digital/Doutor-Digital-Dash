namespace LeadAnalytics.Api.DTOs.Auth;

public class LoginResponseDto
{
    public string UserName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string Role { get; set; } = "user";
    public string TokenType { get; set; } = "Bearer";
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }
    public UnitSelectorOptionDto SelectedUnit { get; set; } = new();
    public List<UnitSelectorOptionDto> AvailableUnits { get; set; } = [];
}
