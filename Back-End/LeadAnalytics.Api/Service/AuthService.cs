using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.DTOs.Auth;
using LeadAnalytics.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace LeadAnalytics.Api.Service;

public class AuthService
{
    private const int ResetCodeValidMinutes = 15;
    private const int MaxResetAttempts = 5;
    private const int ResetCodeCooldownSeconds = 60;

    private readonly AppDbContext _db;
    private readonly JwtTokenService _jwtTokenService;
    private readonly EmailService _emailService;
    private readonly ILogger<AuthService> _logger;

    public AuthService(
        AppDbContext db,
        JwtTokenService jwtTokenService,
        EmailService emailService,
        ILogger<AuthService> logger)
    {
        _db = db;
        _jwtTokenService = jwtTokenService;
        _emailService = emailService;
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

    public async Task<bool> RequestPasswordResetAsync(string emailRaw)
    {
        if (string.IsNullOrWhiteSpace(emailRaw))
        {
            _logger.LogWarning("❌ Solicitação de recuperação sem email");
            return false;
        }

        var email = emailRaw.Trim().ToLowerInvariant();

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == email);

        // Resposta sempre genérica para não vazar quais emails existem.
        if (user == null || !user.IsActive)
        {
            _logger.LogInformation("🔍 Recuperação solicitada para email não cadastrado/inativo: {Email}", email);
            return true;
        }

        if (user.ResetPasswordRequestedAt.HasValue &&
            (DateTime.UtcNow - user.ResetPasswordRequestedAt.Value).TotalSeconds < ResetCodeCooldownSeconds)
        {
            _logger.LogWarning("⏱️ Recuperação solicitada antes do cooldown: {Email}", email);
            return true;
        }

        var code = GenerateNumericCode(6);

        user.ResetPasswordCodeHash = BCrypt.Net.BCrypt.HashPassword(code);
        user.ResetPasswordCodeExpiresAt = DateTime.UtcNow.AddMinutes(ResetCodeValidMinutes);
        user.ResetPasswordAttempts = 0;
        user.ResetPasswordRequestedAt = DateTime.UtcNow;
        user.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        try
        {
            await _emailService.SendPasswordResetCodeAsync(
                user.Email,
                user.Name,
                code,
                ResetCodeValidMinutes);
            _logger.LogInformation("✅ Código de recuperação enviado para {Email}", email);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Falha ao enviar código de recuperação para {Email}", email);
        }

        return true;
    }

    public async Task<bool> VerifyResetCodeAsync(string emailRaw, string code)
    {
        var user = await FindUserForResetAsync(emailRaw);
        if (user == null) return false;

        return ValidateResetCode(user, code, persistFailure: true);
    }

    public async Task<(bool Success, string? Error)> ResetPasswordAsync(string emailRaw, string code, string newPassword)
    {
        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 8)
            return (false, "A nova senha deve ter pelo menos 8 caracteres.");

        var user = await FindUserForResetAsync(emailRaw);
        if (user == null)
            return (false, "Código inválido ou expirado.");

        if (!ValidateResetCode(user, code, persistFailure: false))
        {
            user.ResetPasswordAttempts++;
            await _db.SaveChangesAsync();
            return (false, "Código inválido ou expirado.");
        }

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
        user.ResetPasswordCodeHash = null;
        user.ResetPasswordCodeExpiresAt = null;
        user.ResetPasswordAttempts = 0;
        user.ResetPasswordRequestedAt = null;
        user.FailedLoginAttempts = 0;
        user.LockedUntil = null;
        user.RefreshToken = null;
        user.RefreshTokenExpiresAt = null;
        user.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        _logger.LogInformation("🔑 Senha redefinida com sucesso: {Email}", user.Email);
        return (true, null);
    }

    private async Task<User?> FindUserForResetAsync(string emailRaw)
    {
        if (string.IsNullOrWhiteSpace(emailRaw)) return null;
        var email = emailRaw.Trim().ToLowerInvariant();
        return await _db.Users.FirstOrDefaultAsync(u => u.Email == email);
    }

    private bool ValidateResetCode(User user, string code, bool persistFailure)
    {
        if (string.IsNullOrWhiteSpace(code) ||
            string.IsNullOrEmpty(user.ResetPasswordCodeHash) ||
            !user.ResetPasswordCodeExpiresAt.HasValue)
        {
            return false;
        }

        if (user.ResetPasswordCodeExpiresAt.Value < DateTime.UtcNow)
        {
            _logger.LogWarning("⏰ Código de recuperação expirado: {Email}", user.Email);
            return false;
        }

        if (user.ResetPasswordAttempts >= MaxResetAttempts)
        {
            _logger.LogWarning("🚫 Limite de tentativas de recuperação excedido: {Email}", user.Email);
            return false;
        }

        var ok = BCrypt.Net.BCrypt.Verify(code.Trim(), user.ResetPasswordCodeHash);
        if (!ok && persistFailure)
        {
            user.ResetPasswordAttempts++;
            _db.SaveChanges();
        }

        return ok;
    }

    private static string GenerateNumericCode(int digits)
    {
        var max = (int)Math.Pow(10, digits);
        var n = System.Security.Cryptography.RandomNumberGenerator.GetInt32(0, max);
        return n.ToString().PadLeft(digits, '0');
    }
}