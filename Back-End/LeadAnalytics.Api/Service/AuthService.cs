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
    private readonly GoogleAuthService _googleAuthService;
    private readonly InvitationService _invitationService;
    private readonly LoginSessionService _loginSessions;
    private readonly IHttpContextAccessor _httpContext;
    private readonly ILogger<AuthService> _logger;

    public AuthService(
        AppDbContext db,
        JwtTokenService jwtTokenService,
        EmailService emailService,
        GoogleAuthService googleAuthService,
        InvitationService invitationService,
        LoginSessionService loginSessions,
        IHttpContextAccessor httpContext,
        ILogger<AuthService> logger)
    {
        _db = db;
        _jwtTokenService = jwtTokenService;
        _emailService = emailService;
        _googleAuthService = googleAuthService;
        _invitationService = invitationService;
        _loginSessions = loginSessions;
        _httpContext = httpContext;
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

        if (string.IsNullOrEmpty(user.PasswordHash) ||
            !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
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

        return await BuildLoginResponseAsync(user, "password");
    }

    public async Task<(LoginResponseDto? Result, string? Error)> LoginWithGoogleAsync(string idToken)
    {
        var info = await _googleAuthService.ValidateIdTokenAsync(idToken);
        if (info is null)
            return (null, "Token Google inválido.");

        var user = await _db.Users
            .Include(u => u.Unit)
            .FirstOrDefaultAsync(u => u.Email == info.Email);

        if (user == null)
        {
            // Pode haver convite pendente — mas o fluxo de aceite de convite usa
            // outro endpoint (POST /api/invitations/{token}/accept). Aqui é login
            // direto: exige que o usuário já exista.
            var hasPendingInvite = await _db.Invitations.AnyAsync(i =>
                i.Email == info.Email && i.AcceptedAt == null && i.RevokedAt == null && i.ExpiresAt > DateTime.UtcNow);

            if (hasPendingInvite)
                return (null, "Você tem um convite pendente. Use o link enviado por email para aceitar.");

            return (null, "Sem acesso. Solicite um convite.");
        }

        if (!user.IsActive)
            return (null, "Usuário inativo.");

        // Vincula GoogleSub + avatar no primeiro login (atualiza avatar se mudou)
        var changed = false;
        if (string.IsNullOrEmpty(user.GoogleSub))
        {
            user.GoogleSub = info.Sub;
            user.AuthMethod = "google";
            changed = true;
        }
        if (!string.IsNullOrWhiteSpace(info.Picture) &&
            !string.Equals(user.PhotoPath, info.Picture, StringComparison.Ordinal))
        {
            user.PhotoPath = info.Picture;
            changed = true;
        }
        if (changed)
        {
            user.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }

        var resp = await BuildLoginResponseAsync(user, "google");
        return (resp, null);
    }

    public async Task<(LoginResponseDto? Result, string? Error)> AcceptInvitationWithGoogleAsync(
        string token,
        string idToken,
        CancellationToken ct = default)
    {
        var info = await _googleAuthService.ValidateIdTokenAsync(idToken, ct);
        if (info is null) return (null, "Token Google inválido.");

        var (inv, err) = await _invitationService.AcceptAsync(token, info.Email, ct);
        if (inv is null) return (null, err);

        var existing = await _db.Users
            .Include(u => u.Unit)
            .FirstOrDefaultAsync(u => u.Email == inv.Email, ct);

        User user;
        if (existing == null)
        {
            user = new User
            {
                Email = inv.Email,
                Name = info.Name,
                Role = inv.Role,
                TenantId = inv.TenantId,
                PasswordHash = string.Empty,
                GoogleSub = info.Sub,
                AuthMethod = "google",
                PhotoPath = info.Picture,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            _db.Users.Add(user);
            await _db.SaveChangesAsync(ct);
        }
        else
        {
            user = existing;
            if (string.IsNullOrEmpty(user.GoogleSub))
            {
                user.GoogleSub = info.Sub;
                user.AuthMethod = "google";
            }
            if (!string.IsNullOrWhiteSpace(info.Picture))
            {
                user.PhotoPath = info.Picture;
            }
            // Usuário já existia sem tenant → herda o tenant do convite (senão fica 403 no dashboard).
            if (user.TenantId is null && inv.TenantId > 0)
            {
                user.TenantId = inv.TenantId;
            }
            user.UpdatedAt = DateTime.UtcNow;
        }

        // Garante UserUnit
        var hasLink = await _db.UserUnits
            .AnyAsync(uu => uu.UserId == user.Id && uu.UnitId == inv.UnitId, ct);
        if (!hasLink)
        {
            _db.UserUnits.Add(new UserUnit
            {
                UserId = user.Id,
                UnitId = inv.UnitId,
                CreatedAt = DateTime.UtcNow
            });
        }

        await _db.SaveChangesAsync(ct);
        await _invitationService.MarkAcceptedAsync(inv.Id, ct);

        // Email de boas-vindas — síncrono mas não falha o aceite se cair.
        try
        {
            var unitName = await _db.Units.AsNoTracking()
                .Where(u => u.Id == inv.UnitId)
                .Select(u => u.Name)
                .FirstOrDefaultAsync(ct) ?? "sua unidade";

            var frontendUrl = Environment.GetEnvironmentVariable("FRONTEND_URL")
                ?? "https://dashboard.doutordigitalconsultoria.com";

            await _emailService.SendWelcomeAsync(
                toEmail: user.Email,
                userName: user.Name,
                unitName: unitName,
                dashboardUrl: frontendUrl);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Falha ao enviar email de boas-vindas para {Email}", user.Email);
        }

        var response = await BuildLoginResponseAsync(user, "google");
        return (response, null);
    }

    private async Task<LoginResponseDto> BuildLoginResponseAsync(User user, string authMethod)
    {
        var availableUnits = await GetUserUnitsAsync(user);

        if (availableUnits.Count == 0)
        {
            _logger.LogWarning("❌ Usuário sem unidades: {Email}", user.Email);
            throw new InvalidOperationException("Usuário sem unidades disponíveis.");
        }

        // Self-heal do tenant: usuários convidados (ex.: trafego_pago) podem ter ficado
        // com TenantId null em fluxos antigos de aceite. Sem tenant no JWT, o
        // TenantUnitGuard barra TODAS as leituras do dashboard com 403. Deriva o tenant
        // do ClinicId da unidade do usuário e persiste — conserta no próximo login.
        if (user.TenantId is null)
        {
            var derivedTenant = await _db.UserUnits
                .Where(uu => uu.UserId == user.Id)
                .Join(_db.Units, uu => uu.UnitId, u => u.Id, (uu, u) => (int?)u.ClinicId)
                .FirstOrDefaultAsync();

            if (derivedTenant is not null)
            {
                user.TenantId = derivedTenant;
                _logger.LogInformation(
                    "🔧 TenantId derivado da unidade p/ {Email}: {Tenant}", user.Email, derivedTenant);
            }
        }

        // Cria a sessão de login (IP/UA + GeoIP em background) antes de gerar o
        // token, para embutir o id da sessão (claim "sid") usado pelo heartbeat.
        var (ip, userAgent) = ResolveClient();
        var sessionId = await _loginSessions.StartAsync(user, authMethod, ip, userAgent);

        var (accessToken, expiresAtUtc) = _jwtTokenService.GenerateToken(
            user, availableUnits, authMethod, sessionId);

        var refreshToken = GenerateRefreshToken();

        user.RefreshToken = refreshToken;
        user.RefreshTokenExpiresAt = DateTime.UtcNow.AddDays(30);
        user.LastLoginAt = DateTime.UtcNow;
        user.FailedLoginAttempts = 0;
        user.LockedUntil = null;

        await _db.SaveChangesAsync();

        _logger.LogInformation("✅ Login bem-sucedido: {Email} ({Role}, {Method})",
            user.Email, user.Role, authMethod);

        return new LoginResponseDto
        {
            UserName = user.Name,
            Email = user.Email,
            Role = user.Role,
            PhotoUrl = user.PhotoPath,
            AuthMethod = authMethod,
            TokenType = "Bearer",
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            ExpiresAtUtc = expiresAtUtc,
            SelectedUnit = availableUnits.First(),
            AvailableUnits = availableUnits
        };
    }

    /// <summary>IP (X-Forwarded-For primeiro) + User-Agent do request atual.</summary>
    private (string? Ip, string? UserAgent) ResolveClient()
    {
        var ctx = _httpContext.HttpContext;
        if (ctx is null) return (null, null);

        string? ip = null;
        var fwd = ctx.Request.Headers["X-Forwarded-For"].ToString();
        if (!string.IsNullOrWhiteSpace(fwd))
            ip = fwd.Split(',')[0].Trim();
        if (string.IsNullOrWhiteSpace(ip))
            ip = ctx.Connection.RemoteIpAddress?.ToString();

        var ua = ctx.Request.Headers["User-Agent"].ToString();
        return (ip, string.IsNullOrWhiteSpace(ua) ? null : ua);
    }

    private async Task<List<UnitSelectorOptionDto>> GetUserUnitsAsync(User user)
    {
        var roleLower = (user.Role ?? string.Empty).ToLowerInvariant();
        var isAdminLevel = Roles.IsAdminLevel(user.Role);
        var isSdr = roleLower is "sdr";

        IQueryable<Unit> query;

        if (isAdminLevel)
        {
            query = _db.Units.AsNoTracking();
            _logger.LogInformation("🔓 Admin-level ({Role}): todas unidades", user.Role);
        }
        else
        {
            // Usuário ligado a unidades específicas via UserUnit?
            var hasUserUnits = await _db.UserUnits
                .AsNoTracking()
                .AnyAsync(uu => uu.UserId == user.Id);

            if (hasUserUnits && !isSdr)
            {
                // Convidado: só enxerga as unidades vinculadas
                var unitIds = await _db.UserUnits
                    .AsNoTracking()
                    .Where(uu => uu.UserId == user.Id)
                    .Select(uu => uu.UnitId)
                    .ToListAsync();

                query = _db.Units.AsNoTracking().Where(u => unitIds.Contains(u.Id));
                _logger.LogInformation("🔒 Usuário com UserUnit ({Count} unidades)", unitIds.Count);
            }
            else if (user.TenantId.HasValue)
            {
                // SDR ou usuário "manager" antigo: todas as units do tenant
                query = _db.Units
                    .AsNoTracking()
                    .Where(u => u.ClinicId == user.TenantId.Value);

                _logger.LogInformation("🔒 Tenant-wide: tenant {TenantId}, role {Role}",
                    user.TenantId.Value, user.Role);
            }
            else
            {
                _logger.LogWarning("⚠️ Usuário sem tenant nem UserUnit");
                return [];
            }
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
