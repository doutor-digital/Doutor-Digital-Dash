using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.DTOs.Response;
using LeadAnalytics.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace LeadAnalytics.Api.Service;

public class UnitService(AppDbContext db, ILogger<UnitService> logger)
{
    private readonly AppDbContext _db = db;
    private readonly ILogger<UnitService> _logger = logger;

    public async Task<Unit> GetOrCreateAsync(int clinicId)
    {
        var unit = await _db.Units
            .FirstOrDefaultAsync(u => u.ClinicId == clinicId);

        if (unit is null)
        {
            var name = clinicId == 8020
                ? $"Unidade de Araguaína {clinicId}"
                : $"Unidade {clinicId}";

            unit = new Unit
            {
                ClinicId = clinicId,
                Name = name,
                CreatedAt = DateTime.UtcNow
            };

            _db.Units.Add(unit);
            await _db.SaveChangesAsync();

            _logger.LogInformation("Unidade criada automaticamente: {ClinicId}", clinicId);
        }

        return unit;
    }

    // Lista todas as unidades
    public async Task<List<Unit>> GetAllAsync()
    {
        return await _db.Units
            .OrderBy(u => u.Name)
            .ToListAsync();
    }

    public async Task<IEnumerable<LeadsPorUnidadeDto>> GetQuantityLeadsUnit(int clinicId)
    {
        var resultado = await _db.Leads
            .Where(l => l.TenantId == clinicId)
            .GroupBy(l => l.TenantId)
            .Select(g => new LeadsPorUnidadeDto
            {
                UnitId = g.Key,
                QuantidadeLeads = g.Count()
            })
            .ToListAsync();

        return resultado;
    }
}