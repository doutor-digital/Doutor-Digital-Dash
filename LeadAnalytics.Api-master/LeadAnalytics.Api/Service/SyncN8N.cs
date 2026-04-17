using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.DTOs.Response;
using LeadAnalytics.Api.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace LeadAnalytics.Api.Service;

public class SyncN8N(AppDbContext db)
{
    private readonly AppDbContext _db = db;

    public async Task SyncLead(SyncLeadDto dto)
    {
        var lead = await _db.Leads
            .FirstOrDefaultAsync(l =>
                l.ExternalId == dto.ExternalId &&
                l.TenantId == dto.TenantId);

        if (lead is null)
        {
            // Cria o lead
            _db.Leads.Add(new Lead
            {
                ExternalId = dto.ExternalId,
                TenantId = dto.TenantId,
                Name = dto.Name ?? "Sem nome",
                Phone = dto.Phone ?? "Sem telefone",
                CurrentStage = dto.Stage ?? "SEM_ETAPA",
                Tags = JsonSerializer.Serialize(dto.Tags),
                CreatedAt = dto.CreatedAt ?? DateTime.UtcNow,
                UpdatedAt = dto.UpdatedAt ?? DateTime.UtcNow
            });
        }
        else
        {
            // Atualiza o lead
            if (dto.Name is not null) lead.Name = dto.Name;
            if (dto.Phone is not null) lead.Phone = dto.Phone;
            if (dto.Stage is not null) lead.CurrentStage = dto.Stage;

            // Tags — substitui direto, sem tentar mesclar
            if (dto.Tags is not null && dto.Tags.Count > 0)
                lead.Tags = JsonSerializer.Serialize(dto.Tags);

            lead.UpdatedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync();
    }
}
