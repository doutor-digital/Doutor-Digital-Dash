using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.Models;
using LeadAnalytics.Api.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LeadAnalytics.Api.Service;

/// <summary>
/// Garante, no startup, que cada email em <c>Auth:SuperAdminEmails</c> tem um
/// User com Role = "super_admin" no banco. Sem isso, o primeiro login Google
/// retornaria 403 (não há super-admin pra emitir convites pra ninguém — galinha
/// e ovo).
///
/// Comportamento:
///  - Se o User não existe → cria com Role = super_admin, AuthMethod = google,
///    PasswordHash vazio (ele vai entrar via Google).
///  - Se o User existe mas com outro Role → promove pra super_admin.
///  - Se já é super_admin → nada acontece.
/// </summary>
public class SuperAdminSeedService
{
    public static async Task EnsureAsync(IServiceProvider services, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var opts = scope.ServiceProvider.GetRequiredService<IOptions<AuthOptions>>().Value;
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<SuperAdminSeedService>>();

        var emails = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (opts.SuperAdminEmails != null)
            foreach (var e in opts.SuperAdminEmails)
                if (!string.IsNullOrWhiteSpace(e))
                    emails.Add(e.Trim().ToLowerInvariant());

        if (!string.IsNullOrWhiteSpace(opts.SuperAdminEmailsCsv))
            foreach (var e in opts.SuperAdminEmailsCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                emails.Add(e.ToLowerInvariant());

        if (emails.Count == 0)
        {
            logger.LogInformation("Nenhum super-admin configurado em Auth:SuperAdminEmails");
            return;
        }

        foreach (var email in emails)
        {
            var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);

            if (user == null)
            {
                db.Users.Add(new User
                {
                    Email = email,
                    Name = email.Split('@')[0],
                    Role = "super_admin",
                    PasswordHash = string.Empty,
                    AuthMethod = "google",
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                });
                logger.LogInformation("🌱 Super-admin criado: {Email}", email);
            }
            else if (!string.Equals(user.Role, "super_admin", StringComparison.OrdinalIgnoreCase))
            {
                user.Role = "super_admin";
                user.IsActive = true;
                user.UpdatedAt = DateTime.UtcNow;
                logger.LogInformation("⬆️ Promovido a super-admin: {Email}", email);
            }
        }

        await db.SaveChangesAsync(ct);
    }
}
