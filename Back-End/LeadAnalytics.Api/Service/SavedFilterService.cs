using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace LeadAnalytics.Api.Service;

/// <summary>Item de entrada para criar/atualizar um filtro salvo global.</summary>
public record SavedFilterSaveItem(string Name, string FilterJson, int SortOrder);

/// <summary>
/// CRUD dos filtros dinâmicos globais exibidos no topo do dashboard. São globais
/// (não têm unidade) — o mesmo conjunto aparece para todas as clínicas.
/// </summary>
public class SavedFilterService(AppDbContext db)
{
    private readonly AppDbContext _db = db;

    /// <summary>Todos os filtros salvos, na ordem de exibição.</summary>
    public Task<List<SavedFilter>> ListAsync(CancellationToken ct = default) =>
        _db.SavedFilters.AsNoTracking()
            .OrderBy(f => f.SortOrder)
            .ThenBy(f => f.Id)
            .ToListAsync(ct);

    /// <summary>Cria um novo filtro salvo e devolve a entidade persistida.</summary>
    public async Task<SavedFilter> CreateAsync(SavedFilterSaveItem item, string? email, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var row = new SavedFilter
        {
            Name = item.Name.Trim(),
            FilterJson = item.FilterJson,
            SortOrder = item.SortOrder,
            UpdatedByEmail = email,
            CreatedAt = now,
            UpdatedAt = now,
        };
        _db.SavedFilters.Add(row);
        await _db.SaveChangesAsync(ct);
        return row;
    }

    /// <summary>Atualiza um filtro existente. Devolve null se não existir.</summary>
    public async Task<SavedFilter?> UpdateAsync(int id, SavedFilterSaveItem item, string? email, CancellationToken ct = default)
    {
        var row = await _db.SavedFilters.FirstOrDefaultAsync(f => f.Id == id, ct);
        if (row is null) return null;

        row.Name = item.Name.Trim();
        row.FilterJson = item.FilterJson;
        row.SortOrder = item.SortOrder;
        row.UpdatedByEmail = email;
        row.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return row;
    }

    /// <summary>Remove um filtro. Devolve false se não existir (no-op).</summary>
    public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
    {
        var row = await _db.SavedFilters.FirstOrDefaultAsync(f => f.Id == id, ct);
        if (row is null) return false;
        _db.SavedFilters.Remove(row);
        await _db.SaveChangesAsync(ct);
        return true;
    }
}
