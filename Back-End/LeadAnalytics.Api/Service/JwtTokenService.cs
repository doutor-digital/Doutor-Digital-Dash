using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using LeadAnalytics.Api.DTOs.Auth;
using LeadAnalytics.Api.Models;
using Microsoft.IdentityModel.Tokens;

namespace LeadAnalytics.Api.Service;

public class JwtTokenService(IConfiguration config, ILogger<JwtTokenService> logger)
{
    private readonly IConfiguration _config = config;
    private readonly ILogger<JwtTokenService> _logger = logger;

    /// <summary>
    /// Gerar JWT access token
    /// </summary>
    public (string token, DateTime expiresAtUtc) GenerateToken(
        User user,
        List<UnitSelectorOptionDto> availableUnits)
    {
        // ─────────────────────────────────────────────
        // CONFIG
        // ─────────────────────────────────────────────
        var jwtSecret = _config["Jwt:Secret"];
        var issuer = _config["Jwt:Issuer"] ?? "LeadAnalyticsApi";
        var audience = _config["Jwt:Audience"] ?? "LeadAnalyticsClient";

        if (string.IsNullOrWhiteSpace(jwtSecret))
            throw new InvalidOperationException("JWT Secret não configurado");

        byte[] keyBytes;

        try
        {
            keyBytes = Convert.FromBase64String(jwtSecret);
        }
        catch
        {
            throw new InvalidOperationException("JWT Secret deve estar em Base64 válido");
        }

        if (keyBytes.Length < 16)
            throw new InvalidOperationException("JWT Secret deve ter no mínimo 128 bits (16 bytes)");

        var securityKey = new SymmetricSecurityKey(keyBytes);

        var credentials = new SigningCredentials(
            securityKey,
            SecurityAlgorithms.HmacSha256
        );

        // ─────────────────────────────────────────────
        // CLAIMS
        // ─────────────────────────────────────────────
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Name, user.Name),
            new(ClaimTypes.Role, user.Role)
        };

        if (user.TenantId.HasValue)
        {
            claims.Add(new Claim("tenant_id", user.TenantId.Value.ToString()));
        }

        var unitsJson = System.Text.Json.JsonSerializer.Serialize(
            availableUnits.Select(u => new { u.Id, u.ClinicId, u.Name })
        );

        claims.Add(new Claim("available_units", unitsJson));

        // ─────────────────────────────────────────────
        // TOKEN
        // ─────────────────────────────────────────────
        var expiresAt = DateTime.UtcNow.AddHours(1);

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: expiresAt,
            signingCredentials: credentials
        );

        var tokenString = new JwtSecurityTokenHandler().WriteToken(token);

        _logger.LogInformation(
            "✅ Token gerado: User={UserId}, Role={Role}, Expires={ExpiresAt}",
            user.Id,
            user.Role,
            expiresAt
        );

        return (tokenString, expiresAt);
    }
}