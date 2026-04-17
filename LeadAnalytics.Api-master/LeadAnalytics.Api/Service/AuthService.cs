using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.DTOs.Auth;
using LeadAnalytics.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace LeadAnalytics.Api.Service;

public class AuthService
{
    private readonly AppDbContext _db;
    private readonly JwtTokenService _jwtTokenService;
    private readonly ILogger<AuthService> _logger;

    public AuthService(
        AppDbContext db,
        JwtTokenService jwtTokenService,
        ILogger<AuthService> logger)
    {
        _db = db;
        _jwtTokenService = jwtTokenService;
        _logger = logger;
    }

    public async Task<LoginResponseDto?> LoginAsync(LoginRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.Email) ||
            string.IsNullOrWhiteSpace(request.Password))
        {
            _logger.LogWarning("❌ Login sem email ou senha");
            return null;
        }

        var email = request.Email.Trim().ToLowerInvariant();

        var user = await _db.Users
            .Include(u => u.Unit)
            .FirstOrDefaultAsync(u => u.Email == email);

        if (user == null)
        {
            _logger.LogWarning("❌ Usuário não encontrado: {Email}", email);
            return null;
        }

        if (!BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
        {
            _logger.LogWarning("❌ Senha incorreta: {Email}", email);
            await HandleFailedLoginAsync(user);
            return null;
        }

        if (user.LockedUntil.HasValue && user.LockedUntil > DateTime.UtcNow)
        {
            _logger.LogWarning("🔒 Usuário bloqueado: {Email}", email);
            return null;
        }

        if (!user.IsActive)
        {
            _logger.LogWarning("⛔ Usuário inativo: {Email}", email);
            return null;
        }

        var availableUnits = await GetUserUnitsAsync(user);

        if (availableUnits.Count == 0)
        {
            _logger.LogWarning("❌ Usuário sem unidades: {Email}", email);
            return null;
        }

        var (accessToken, expiresAtUtc) = _jwtTokenService.GenerateToken(
            user,
            availableUnits);

        var refreshToken = GenerateRefreshToken();

        user.RefreshToken = refreshToken;
        user.RefreshTokenExpiresAt = DateTime.UtcNow.AddDays(30);
        user.LastLoginAt = DateTime.UtcNow;
        user.FailedLoginAttempts = 0;
        user.LockedUntil = null;

        await _db.SaveChangesAsync();

        _logger.LogInformation("✅ Login bem-sucedido: {Email} ({Role})", email, user.Role);

        return new LoginResponseDto
        {
            UserName = user.Name,
            Email = user.Email,
            Role = user.Role,
            TokenType = "Bearer",
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            ExpiresAtUtc = expiresAtUtc,
            SelectedUnit = availableUnits.First(),
            AvailableUnits = availableUnits
        };
    }

    private async Task<List<UnitSelectorOptionDto>> GetUserUnitsAsync(User user)
    {
        IQueryable<Unit> query;

        if (user.Role == "super_admin")
        {
            query = _db.Units.AsNoTracking();

            _logger.LogInformation("🔓 Super admin: todas unidades");
        }
        else if (user.TenantId.HasValue)
        {
            query = _db.Units
                .AsNoTracking()
                .Where(u => u.ClinicId == user.TenantId.Value);

            _logger.LogInformation(
                "🔒 Usuário comum: tenant {TenantId}",
                user.TenantId.Value);
        }
        else
        {
            _logger.LogWarning("⚠️ Usuário sem tenant");
            return [];
        }

        var units = await query
            .Select(u => new UnitSelectorOptionDto
            {
                Id = u.Id,
                ClinicId = u.ClinicId,
                Name = u.Name,
                IsDefault = false
            })
            .ToListAsync();

        if (units.Count > 0)
            units[0].IsDefault = true;

        return units;
    }

    private async Task HandleFailedLoginAsync(User user)
    {
        user.FailedLoginAttempts++;

        if (user.FailedLoginAttempts >= 5)
        {
            user.LockedUntil = DateTime.UtcNow.AddMinutes(15);
            _logger.LogWarning("🔒 Bloqueado: {Email}", user.Email);
        }

        await _db.SaveChangesAsync();
    }

    private static string GenerateRefreshToken()
    {
        var randomNumber = new byte[32];
        using var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
        rng.GetBytes(randomNumber);
        return Convert.ToBase64String(randomNumber);
    }
}