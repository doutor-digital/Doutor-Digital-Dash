using System.Security.Cryptography;
using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.DTOs.Invitations;
using LeadAnalytics.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace LeadAnalytics.Api.Service;

public class InvitationService
{
    private const int TokenBytes = 32;
    private const int InvitationValidHours = 72;

    private readonly AppDbContext _db;
    private readonly EmailService _emailService;
    private readonly IConfiguration _config;
    private readonly ILogger<InvitationService> _logger;

    public InvitationService(
        AppDbContext db,
        EmailService emailService,
        IConfiguration config,
        ILogger<InvitationService> logger)
    {
        _db = db;
        _emailService = emailService;
        _config = config;
        _logger = logger;
    }

    public async Task<(InvitationCreateResponseDto? Result, string? Error)> CreateAsync(
        InvitationCreateDto dto,
        User caller,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(dto.Email))
            return (null, "Email é obrigatório.");
        if (dto.UnitId <= 0)
            return (null, "UnitId inválido.");

        var role = Roles.Canonical(dto.Role ?? Roles.UnitUser);
        if (string.IsNullOrEmpty(role)) role = Roles.UnitUser;
        if (!Roles.IsValidInviteRole(role))
            return (null, "Role inválido (use unit_user, sdr, manager, trafego_pago ou analista_ti).");

        // Apenas admin-level (super_admin / analista_ti) pode conceder analista_ti.
        if (Roles.IsAnalistaTi(role) && !Roles.IsAdminLevel(caller.Role))
            return (null, "Apenas super_admin ou analista_ti pode conceder o papel analista_ti.");

        var email = dto.Email.Trim().ToLowerInvariant();

        var unit = await _db.Units.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == dto.UnitId, ct);
        if (unit is null)
            return (null, "Unidade não encontrada.");

        var isSuperAdmin = string.Equals(caller.Role, "super_admin", StringComparison.OrdinalIgnoreCase);
        if (!isSuperAdmin)
        {
            if (!caller.TenantId.HasValue)
                return (null, "Usuário sem tenant não pode convidar.");
            if (unit.ClinicId != caller.TenantId.Value)
                return (null, "Unidade não pertence ao seu tenant.");
        }

        // Existe usuário com esse email já vinculado a essa unidade?
        var existingUser = await _db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Email == email, ct);

        if (existingUser != null)
        {
            var alreadyOnUnit = await _db.UserUnits
                .AnyAsync(uu => uu.UserId == existingUser.Id && uu.UnitId == dto.UnitId, ct);
            if (alreadyOnUnit)
                return (null, "Usuário já tem acesso a essa unidade.");
        }

        // Existe convite pendente?
        var pending = await _db.Invitations
            .FirstOrDefaultAsync(i => i.Email == email
                && i.UnitId == dto.UnitId
                && i.AcceptedAt == null
                && i.RevokedAt == null, ct);

        // Gera token
        var token = GenerateToken();
        var tokenHash = HashToken(token);

        if (pending != null)
        {
            // Reutiliza o registro: troca token, renova prazo
            pending.TokenHash = tokenHash;
            pending.ExpiresAt = DateTime.UtcNow.AddHours(InvitationValidHours);
            pending.Role = role;
            pending.CreatedByUserId = caller.Id;
            await _db.SaveChangesAsync(ct);
        }
        else
        {
            pending = new Invitation
            {
                Email = email,
                TenantId = unit.ClinicId,
                UnitId = dto.UnitId,
                Role = role,
                TokenHash = tokenHash,
                ExpiresAt = DateTime.UtcNow.AddHours(InvitationValidHours),
                CreatedByUserId = caller.Id,
                CreatedAt = DateTime.UtcNow
            };
            _db.Invitations.Add(pending);
            await _db.SaveChangesAsync(ct);
        }

        var frontendUrl = GetFrontendUrl();
        var acceptUrl = $"{frontendUrl.TrimEnd('/')}/invite/{token}";

        try
        {
            await _emailService.SendInvitationAsync(
                email, caller.Name, unit.Name ?? "Unidade", role, acceptUrl, InvitationValidHours);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao enviar email de convite para {Email}", email);
            // não falha a criação — admin pode reenviar
        }

        return (new InvitationCreateResponseDto
        {
            Id = pending.Id,
            Email = pending.Email,
            AcceptUrl = acceptUrl,
            ExpiresAt = pending.ExpiresAt
        }, null);
    }

    public async Task<List<InvitationListItemDto>> ListPendingAsync(
        User caller,
        int? unitId,
        CancellationToken ct = default)
    {
        var isSuperAdmin = string.Equals(caller.Role, "super_admin", StringComparison.OrdinalIgnoreCase);

        var query = _db.Invitations.AsNoTracking()
            .Where(i => i.AcceptedAt == null && i.RevokedAt == null);

        if (!isSuperAdmin)
        {
            if (!caller.TenantId.HasValue) return new();
            query = query.Where(i => i.TenantId == caller.TenantId.Value);
        }

        if (unitId.HasValue) query = query.Where(i => i.UnitId == unitId.Value);

        var data = await query
            .OrderByDescending(i => i.CreatedAt)
            .Take(200)
            .Join(_db.Units.AsNoTracking(),
                i => i.UnitId, u => u.Id,
                (i, u) => new { i, unitName = u.Name })
            .GroupJoin(_db.Users.AsNoTracking(),
                x => x.i.CreatedByUserId, u => u.Id,
                (x, u) => new { x.i, x.unitName, createdBy = u.Select(z => z.Name).FirstOrDefault() })
            .ToListAsync(ct);

        return data.Select(x => new InvitationListItemDto
        {
            Id = x.i.Id,
            Email = x.i.Email,
            TenantId = x.i.TenantId,
            UnitId = x.i.UnitId,
            UnitName = x.unitName,
            Role = x.i.Role,
            ExpiresAt = x.i.ExpiresAt,
            CreatedAt = x.i.CreatedAt,
            CreatedByName = x.createdBy
        }).ToList();
    }

    public async Task<bool> RevokeAsync(int invitationId, User caller, CancellationToken ct = default)
    {
        var inv = await _db.Invitations.FirstOrDefaultAsync(i => i.Id == invitationId, ct);
        if (inv is null) return false;

        var isSuperAdmin = string.Equals(caller.Role, "super_admin", StringComparison.OrdinalIgnoreCase);
        if (!isSuperAdmin && (caller.TenantId is null || inv.TenantId != caller.TenantId.Value))
            return false;

        inv.RevokedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<InvitationInfoDto?> GetInfoByTokenAsync(string token, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        var hash = HashToken(token);

        var inv = await _db.Invitations.AsNoTracking()
            .FirstOrDefaultAsync(i => i.TokenHash == hash, ct);
        if (inv is null) return null;
        if (inv.AcceptedAt != null || inv.RevokedAt != null) return null;
        if (inv.ExpiresAt < DateTime.UtcNow) return null;

        var unitName = await _db.Units.AsNoTracking()
            .Where(u => u.Id == inv.UnitId)
            .Select(u => u.Name)
            .FirstOrDefaultAsync(ct);

        return new InvitationInfoDto
        {
            Email = inv.Email,
            UnitName = unitName,
            Role = inv.Role,
            ExpiresAt = inv.ExpiresAt
        };
    }

    public async Task<(Invitation? Invitation, string? Error)> AcceptAsync(
        string token,
        string googleEmail,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token)) return (null, "Token inválido.");
        if (string.IsNullOrWhiteSpace(googleEmail)) return (null, "Email inválido.");

        var hash = HashToken(token);
        var inv = await _db.Invitations.FirstOrDefaultAsync(i => i.TokenHash == hash, ct);
        if (inv is null) return (null, "Convite não encontrado.");
        if (inv.AcceptedAt != null) return (null, "Convite já aceito.");
        if (inv.RevokedAt != null) return (null, "Convite revogado.");
        if (inv.ExpiresAt < DateTime.UtcNow) return (null, "Convite expirado.");

        var emailLower = googleEmail.Trim().ToLowerInvariant();
        if (!string.Equals(emailLower, inv.Email, StringComparison.Ordinal))
            return (null, "O email do Google não confere com o do convite.");

        return (inv, null);
    }

    public async Task MarkAcceptedAsync(int invitationId, CancellationToken ct = default)
    {
        var inv = await _db.Invitations.FirstOrDefaultAsync(i => i.Id == invitationId, ct);
        if (inv == null) return;
        inv.AcceptedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    private static string GenerateToken()
    {
        var bytes = new byte[TokenBytes];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }

    public static string HashToken(string token)
    {
        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes);
    }

    private string GetFrontendUrl()
    {
        return Environment.GetEnvironmentVariable("FRONTEND_URL")
            ?? _config["App:FrontendUrl"]
            ?? "http://localhost:5173";
    }
}
