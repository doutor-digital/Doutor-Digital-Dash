using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace LeadAnalytics.Api.Service.Ai;

/// <summary>
/// Guarda, por unidade, o <c>stage_id</c> da Kommo que representa a
/// "Etapa de Entrada" do funil. É lido pela análise da I.A. pra contar
/// SOMENTE os leads que entraram nessa etapa no período (não leads
/// movidos entre outras etapas). Sem isso, a contagem fica inflada.
///
/// Chave em <c>AppConfiguration</c>: <c>kommo.entry_stage.{unitId}</c>.
/// </summary>
public class UnitEntryStageConfig(AppDbContext db)
{
    private static string Key(int unitId) => $"kommo.entry_stage.{unitId}";

    public async Task<int?> GetAsync(int unitId, CancellationToken ct)
    {
        var row = await db.AppConfigurations.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Key == Key(unitId), ct);
        if (row is null) return null;
        return int.TryParse(row.Value, out var id) ? id : null;
    }

    public async Task SetAsync(int unitId, int stageId, CancellationToken ct)
    {
        var k = Key(unitId);
        var existing = await db.AppConfigurations.FirstOrDefaultAsync(c => c.Key == k, ct);
        var now = DateTime.UtcNow;
        if (existing is null)
        {
            db.AppConfigurations.Add(new AppConfiguration
            {
                Key = k,
                Value = stageId.ToString(),
                CreatedAt = now,
                UpdatedAt = now,
            });
        }
        else
        {
            existing.Value = stageId.ToString();
            existing.UpdatedAt = now;
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(int unitId, CancellationToken ct)
    {
        var existing = await db.AppConfigurations.FirstOrDefaultAsync(c => c.Key == Key(unitId), ct);
        if (existing is null) return;
        db.AppConfigurations.Remove(existing);
        await db.SaveChangesAsync(ct);
    }
}
