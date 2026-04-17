using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.DTOs.User;
using LeadAnalytics.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace LeadAnalytics.Api.Service;

public class UserService(AppDbContext db)
{
	private readonly AppDbContext _db = db;
	public async Task<List<UserResponseDto>> GetAllAsync()
	{
		return await _db.Users
			.AsNoTracking()
			.OrderBy(u => u.Name)
			.Select(u => ToResponseDto(u))
			.ToListAsync();
	}

	public async Task<UserResponseDto?> GetByIdAsync(int id)
	{
		var user = await _db.Users
			.AsNoTracking()
			.FirstOrDefaultAsync(u => u.Id == id);

		return user is null ? null : ToResponseDto(user);
	}

	public async Task<UserResponseDto?> CreateAsync(CreateUserDto dto)
	{
		var email = dto.Email.Trim().ToLowerInvariant();

		var emailExists = await _db.Users
			.AnyAsync(u => u.Email == email);

		if (emailExists)
			return null;

        var user = new User
        {
            Name = dto.Name.Trim(),
            Email = email,
            Role = string.IsNullOrWhiteSpace(dto.Role) ? "user" : dto.Role.Trim().ToLowerInvariant(),
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password)
        };

        _db.Users.Add(user);
		await _db.SaveChangesAsync();

		return ToResponseDto(user);
	}

	public async Task<UserResponseDto?> UpdateAsync(int id, UpdateUserDto dto)
	{
		var user = await _db.Users
			.FirstOrDefaultAsync(u => u.Id == id);

		if (user is null)
			return null;

		if (!string.IsNullOrWhiteSpace(dto.Email))
		{
			var normalizedEmail = dto.Email.Trim().ToLowerInvariant();

			var emailInUse = await _db.Users
				.AnyAsync(u => u.Email == normalizedEmail && u.Id != id);

			if (emailInUse)
				return null;

			user.Email = normalizedEmail;
		}

		if (!string.IsNullOrWhiteSpace(dto.Name))
			user.Name = dto.Name.Trim();

		if (!string.IsNullOrWhiteSpace(dto.Role))
			user.Role = dto.Role.Trim().ToLowerInvariant();

		if (!string.IsNullOrWhiteSpace(dto.Password))
			user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password);

		await _db.SaveChangesAsync();

		return ToResponseDto(user);
	}

	public async Task<bool> DeleteAsync(int id)
	{
		var user = await _db.Users
			.FirstOrDefaultAsync(u => u.Id == id);

		if (user is null)
			return false;

		_db.Users.Remove(user);
		await _db.SaveChangesAsync();

		return true;
	}

	private static UserResponseDto ToResponseDto(User user)
	{
		return new UserResponseDto
		{
			Id = user.Id,
			Name = user.Name,
			Email = user.Email,
			Role = user.Role
		};
	}
}
