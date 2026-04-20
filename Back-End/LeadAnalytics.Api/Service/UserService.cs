using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.DTOs.User;
using LeadAnalytics.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace LeadAnalytics.Api.Service;

public class UserService(
	AppDbContext db,
	IWebHostEnvironment env,
	IHttpContextAccessor httpContext,
	ILogger<UserService> logger)
{
	private readonly AppDbContext _db = db;
	private readonly IWebHostEnvironment _env = env;
	private readonly IHttpContextAccessor _httpContext = httpContext;
	private readonly ILogger<UserService> _logger = logger;

	private static readonly string[] AllowedImageExtensions =
		[".jpg", ".jpeg", ".png", ".webp", ".gif"];
	private const long MaxAvatarBytes = 5 * 1024 * 1024; // 5 MB

	public async Task<List<UserResponseDto>> GetAllAsync()
	{
		return await _db.Users
			.AsNoTracking()
			.OrderBy(u => u.Name)
			.Select(u => ToResponseDto(u, BuildBaseUrl()))
			.ToListAsync();
	}

	public async Task<UserResponseDto?> GetByIdAsync(int id)
	{
		var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id);
		return user is null ? null : ToResponseDto(user, BuildBaseUrl());
	}

	public async Task<UserResponseDto?> CreateAsync(CreateUserDto dto)
	{
		var email = dto.Email.Trim().ToLowerInvariant();

		var emailExists = await _db.Users.AnyAsync(u => u.Email == email);
		if (emailExists) return null;

		var user = new User
		{
			Name = dto.Name.Trim(),
			Email = email,
			Role = string.IsNullOrWhiteSpace(dto.Role) ? "user" : dto.Role.Trim().ToLowerInvariant(),
			PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password)
		};

		_db.Users.Add(user);
		await _db.SaveChangesAsync();

		return ToResponseDto(user, BuildBaseUrl());
	}

	public async Task<UserResponseDto?> UpdateAsync(int id, UpdateUserDto dto)
	{
		var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id);
		if (user is null) return null;

		if (!string.IsNullOrWhiteSpace(dto.Email))
		{
			var normalizedEmail = dto.Email.Trim().ToLowerInvariant();
			var emailInUse = await _db.Users.AnyAsync(u => u.Email == normalizedEmail && u.Id != id);
			if (emailInUse) return null;
			user.Email = normalizedEmail;
		}

		if (!string.IsNullOrWhiteSpace(dto.Name)) user.Name = dto.Name.Trim();
		if (!string.IsNullOrWhiteSpace(dto.Role)) user.Role = dto.Role.Trim().ToLowerInvariant();
		if (!string.IsNullOrWhiteSpace(dto.Password))
			user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password);
		if (dto.Phone is not null) user.Phone = string.IsNullOrWhiteSpace(dto.Phone) ? null : dto.Phone.Trim();

		user.UpdatedAt = DateTime.UtcNow;
		await _db.SaveChangesAsync();

		return ToResponseDto(user, BuildBaseUrl());
	}

	public async Task<bool> DeleteAsync(int id)
	{
		var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id);
		if (user is null) return false;

		_db.Users.Remove(user);
		await _db.SaveChangesAsync();
		return true;
	}

	// ─── Perfil do usuário logado ────────────────────────────────────

	public async Task<UserResponseDto?> GetMeAsync(int userId)
	{
		var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
		return user is null ? null : ToResponseDto(user, BuildBaseUrl());
	}

	public async Task<(UserResponseDto? user, string? error)> UpdateMyProfileAsync(
		int userId, UpdateMyProfileDto dto)
	{
		var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId);
		if (user is null) return (null, "usuário não encontrado");

		if (!string.IsNullOrWhiteSpace(dto.Email))
		{
			var normalizedEmail = dto.Email.Trim().ToLowerInvariant();
			var emailInUse = await _db.Users.AnyAsync(u => u.Email == normalizedEmail && u.Id != userId);
			if (emailInUse) return (null, "e-mail já em uso");
			user.Email = normalizedEmail;
		}

		if (!string.IsNullOrWhiteSpace(dto.Name)) user.Name = dto.Name.Trim();
		if (dto.Phone is not null) user.Phone = string.IsNullOrWhiteSpace(dto.Phone) ? null : dto.Phone.Trim();

		user.UpdatedAt = DateTime.UtcNow;
		await _db.SaveChangesAsync();

		return (ToResponseDto(user, BuildBaseUrl()), null);
	}

	public async Task<(bool ok, string? error)> ChangeMyPasswordAsync(int userId, ChangeMyPasswordDto dto)
	{
		var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId);
		if (user is null) return (false, "usuário não encontrado");

		if (!BCrypt.Net.BCrypt.Verify(dto.CurrentPassword, user.PasswordHash))
			return (false, "senha atual incorreta");

		user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.NewPassword);
		user.UpdatedAt = DateTime.UtcNow;
		await _db.SaveChangesAsync();
		return (true, null);
	}

	public async Task<(UserResponseDto? user, string? error)> SetMyAvatarAsync(int userId, IFormFile file)
	{
		if (file.Length <= 0) return (null, "arquivo vazio");
		if (file.Length > MaxAvatarBytes) return (null, "arquivo excede 5MB");

		var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
		if (string.IsNullOrEmpty(ext) || !AllowedImageExtensions.Contains(ext))
			return (null, "formato inválido. Use jpg, jpeg, png, webp ou gif");

		var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId);
		if (user is null) return (null, "usuário não encontrado");

		var webRoot = string.IsNullOrWhiteSpace(_env.WebRootPath)
			? Path.Combine(_env.ContentRootPath, "wwwroot")
			: _env.WebRootPath;

		var avatarsDir = Path.Combine(webRoot, "avatars");
		Directory.CreateDirectory(avatarsDir);

		// Remove avatar antigo, se existir e apontar para wwwroot/avatars
		if (!string.IsNullOrEmpty(user.PhotoPath) && user.PhotoPath.StartsWith("/avatars/", StringComparison.OrdinalIgnoreCase))
		{
			var oldFile = Path.Combine(webRoot, user.PhotoPath.TrimStart('/'));
			try { if (File.Exists(oldFile)) File.Delete(oldFile); }
			catch (Exception ex) { _logger.LogWarning(ex, "Falha ao remover avatar antigo"); }
		}

		var filename = $"user-{userId}-{DateTime.UtcNow:yyyyMMddHHmmssfff}{ext}";
		var destPath = Path.Combine(avatarsDir, filename);

		await using (var stream = File.Create(destPath))
		{
			await file.CopyToAsync(stream);
		}

		user.PhotoPath = $"/avatars/{filename}";
		user.UpdatedAt = DateTime.UtcNow;
		await _db.SaveChangesAsync();

		return (ToResponseDto(user, BuildBaseUrl()), null);
	}

	public async Task<(bool ok, string? error)> RemoveMyAvatarAsync(int userId)
	{
		var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId);
		if (user is null) return (false, "usuário não encontrado");

		if (!string.IsNullOrEmpty(user.PhotoPath) && user.PhotoPath.StartsWith("/avatars/", StringComparison.OrdinalIgnoreCase))
		{
			var webRoot = string.IsNullOrWhiteSpace(_env.WebRootPath)
				? Path.Combine(_env.ContentRootPath, "wwwroot")
				: _env.WebRootPath;
			var oldFile = Path.Combine(webRoot, user.PhotoPath.TrimStart('/'));
			try { if (File.Exists(oldFile)) File.Delete(oldFile); }
			catch (Exception ex) { _logger.LogWarning(ex, "Falha ao remover avatar"); }
		}

		user.PhotoPath = null;
		user.UpdatedAt = DateTime.UtcNow;
		await _db.SaveChangesAsync();
		return (true, null);
	}

	// ─── Helpers ─────────────────────────────────────────────────────

	private string? BuildBaseUrl()
	{
		var req = _httpContext.HttpContext?.Request;
		if (req is null) return null;
		return $"{req.Scheme}://{req.Host}";
	}

	private static UserResponseDto ToResponseDto(User user, string? baseUrl)
	{
		string? photoUrl = null;
		if (!string.IsNullOrEmpty(user.PhotoPath))
		{
			photoUrl = user.PhotoPath.StartsWith("http", StringComparison.OrdinalIgnoreCase)
				? user.PhotoPath
				: (baseUrl is null ? user.PhotoPath : baseUrl + user.PhotoPath);
		}

		return new UserResponseDto
		{
			Id = user.Id,
			Name = user.Name,
			Email = user.Email,
			Role = user.Role,
			Phone = user.Phone,
			PhotoUrl = photoUrl,
			TenantId = user.TenantId,
		};
	}
}
