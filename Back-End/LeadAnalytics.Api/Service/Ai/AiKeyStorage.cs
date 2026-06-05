using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace LeadAnalytics.Api.Service.Ai;

/// <summary>
/// Guarda a API key da OpenAI por tenant na tabela <c>AppConfiguration</c>,
/// cifrada com <see cref="ProtectedTokenService"/> (DataProtection do .NET).
/// Chave canônica: <c>ai.openai.{tenantId}</c>.
///
/// Nada além do tenant dono enxerga o valor decifrado; o front só recebe
/// um bool "está configurada" — nunca o texto.
/// </summary>
public class AiKeyStorage(AppDbContext db, ProtectedTokenService protector)
{
    private static string Key(int tenantId) => $"ai.openai.{tenantId}";

    public async Task<string?> GetAsync(int tenantId, CancellationToken ct)
    {
        var row = await db.AppConfigurations.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Key == Key(tenantId), ct);
        return row is null ? null : protector.Unprotect(row.Value);
    }

    public async Task<bool> HasKeyAsync(int tenantId, CancellationToken ct) =>
        await db.AppConfigurations.AsNoTracking().AnyAsync(c => c.Key == Key(tenantId), ct);

    public async Task SetAsync(int tenantId, string apiKey, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var cipher = protector.Protect(apiKey) ?? throw new InvalidOperationException("falha ao cifrar a key");
        var k = Key(tenantId);
        var existing = await db.AppConfigurations.FirstOrDefaultAsync(c => c.Key == k, ct);
        if (existing is null)
        {
            db.AppConfigurations.Add(new AppConfiguration
            {
                Key = k, Value = cipher, CreatedAt = now, UpdatedAt = now,
            });
        }
        else
        {
            existing.Value = cipher;
            existing.UpdatedAt = now;
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(int tenantId, CancellationToken ct)
    {
        var k = Key(tenantId);
        var existing = await db.AppConfigurations.FirstOrDefaultAsync(c => c.Key == k, ct);
        if (existing is null) return;
        db.AppConfigurations.Remove(existing);
        await db.SaveChangesAsync(ct);
    }
}
