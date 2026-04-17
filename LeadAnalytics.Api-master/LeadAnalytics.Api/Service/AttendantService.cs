using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace LeadAnalytics.Api.Service;

public class AttendantService(AppDbContext db, ILogger<AttendantService> logger)
{
    private readonly AppDbContext _db = db;
    private readonly ILogger<AttendantService> _logger = logger;

    public async Task<Attendant> GetOrCreateAsync(int externalId, string name, string? email, int unitId)
    {
        var attendant = await _db.Attendants
            .FirstOrDefaultAsync(a => a.ExternalId == externalId);

        if (attendant is null)
        {
            attendant = new Attendant
            {
                ExternalId = externalId,
                Name = name,
                Email = email,
                CreatedAt = DateTime.UtcNow,
                UnitId = unitId

            };

            _db.Attendants.Add(attendant);
            await _db.SaveChangesAsync();

            _logger.LogInformation("Atendente criado: {Name} ({ExternalId})", name, externalId);
        }

        return attendant;
    }

    public async Task<List<Attendant>> GetAllAsync()
    {
        return await _db.Attendants
            .OrderBy(a => a.Name)
            .ToListAsync();
    }

    // Busca atribuições de um lead
    public async Task<List<LeadAssignment>> GetAssignmentsByLeadAsync(int externalLeadId, int clinicId)
    {
        return await _db.LeadAssignments
            .Include(a => a.Attendant)
            .Include(a => a.Lead)
            .Where(a =>
                a.Lead.ExternalId == externalLeadId &&
                a.Lead.TenantId == clinicId)
            .OrderByDescending(a => a.AssignedAt)
            .ToListAsync();
    }

    // Ranking de atendentes por conversão
    public async Task<List<object>> GetRankingAsync(int clinicId)
    {
        return await _db.LeadAssignments
            .Include(a => a.Attendant)
            .Include(a => a.Lead)
            .Where(a => a.Lead.TenantId == clinicId)
            .GroupBy(a => new { a.AttendantId, a.Attendant.Name })
            .Select(g => new
            {
                Atendente = g.Key.Name,
                TotalLeads = g.Count(),
                Convertidos = g.Count(a =>
                    a.Lead.CurrentStage == "09_FECHOU_TRATAMENTO" ||
                    a.Lead.CurrentStage == "10_EM_TRATAMENTO" ||
                    a.Lead.CurrentStage == "05_AGENDADO_COM_PAGAMENTO"),
                Agendados = g.Count(a =>
                    a.Lead.CurrentStage == "04_AGENDADO_SEM_PAGAMENTO" ||
                    a.Lead.CurrentStage == "05_AGENDADO_COM_PAGAMENTO")
            })
            .OrderByDescending(x => x.Convertidos)
            .ToListAsync<object>();
    }
}