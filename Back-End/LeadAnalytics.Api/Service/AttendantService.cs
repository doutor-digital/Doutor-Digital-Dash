using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.DTOs.Response;
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
    public async Task<List<AttendantRankingDto>> GetRankingAsync(int? clinicId = null)
    {
        var query = _db.LeadAssignments
            .AsNoTracking()
            .Include(a => a.Attendant)
            .Include(a => a.Lead)
            .AsQueryable();

        if (clinicId.HasValue && clinicId.Value > 0)
            query = query.Where(a => a.Lead.TenantId == clinicId.Value);

        var grouped = await query
            .GroupBy(a => new { a.AttendantId, a.Attendant.Name, a.Attendant.Email })
            .Select(g => new
            {
                g.Key.AttendantId,
                g.Key.Name,
                g.Key.Email,
                Total = g.Count(),
                Agendado = g.Count(a =>
                    a.Lead.CurrentStage == "04_AGENDADO_SEM_PAGAMENTO" ||
                    a.Lead.CurrentStage == "05_AGENDADO_COM_PAGAMENTO"),
                Pago = g.Count(a => a.Lead.HasPayment),
                Tratamento = g.Count(a =>
                    a.Lead.CurrentStage == "09_FECHOU_TRATAMENTO" ||
                    a.Lead.CurrentStage == "10_EM_TRATAMENTO"),
                Conversions = g.Count(a =>
                    a.Lead.CurrentStage == "05_AGENDADO_COM_PAGAMENTO" ||
                    a.Lead.CurrentStage == "09_FECHOU_TRATAMENTO" ||
                    a.Lead.CurrentStage == "10_EM_TRATAMENTO"),
                Active = g.Count(a => a.Lead.ConversationState == "service"),
                First = g.Min(a => (DateTime?)a.AssignedAt),
                Last = g.Max(a => (DateTime?)a.AssignedAt),
            })
            .ToListAsync();

        return grouped
            .Select(g => new AttendantRankingDto
            {
                AttendantId = g.AttendantId,
                Name = g.Name,
                Email = g.Email,
                Total = g.Total,
                Agendado = g.Agendado,
                Pago = g.Pago,
                Tratamento = g.Tratamento,
                Conversions = g.Conversions,
                Active = g.Active,
                AgendadoRate = g.Total == 0 ? 0 : g.Agendado * 100d / g.Total,
                PagoRate = g.Total == 0 ? 0 : g.Pago * 100d / g.Total,
                ConversionRate = g.Total == 0 ? 0 : g.Conversions * 100d / g.Total,
                FirstAssignedAt = g.First,
                LastAssignedAt = g.Last,
            })
            .OrderByDescending(x => x.Conversions)
            .ThenByDescending(x => x.Total)
            .ToList();
    }
}