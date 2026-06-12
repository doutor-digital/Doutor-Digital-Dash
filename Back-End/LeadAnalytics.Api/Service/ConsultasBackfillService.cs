using LeadAnalytics.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace LeadAnalytics.Api.Service;

/// <summary>
/// Backfill local do card Consultas: percorre os leads da unidade que JÁ ESTÃO no
/// banco e popula <see cref="Models.Lead.AppointmentScheduledAt"/> /
/// <see cref="Models.Lead.ConsultationValue"/> a partir do <c>CustomFieldsJson</c>
/// existente, usando o mapeamento de Perfil do Lead da unidade
/// (<see cref="KpiConfigService.LeadProfileFields.AppointmentFieldId"/> /
/// <see cref="KpiConfigService.LeadProfileFields.ValorConsultaFieldId"/>).
///
/// NÃO chama a Kommo — só processa o JSON já capturado pelo webhook/sync anterior.
/// É o caminho rápido pra leads ANTIGOS aparecerem no card depois que o analista
/// acabou de mapear o campo (leads novos já vêm certos via KommoIngestionService).
/// Idempotente: rerodar não muda nada.
/// </summary>
public class ConsultasBackfillService(
    AppDbContext db,
    KpiConfigService kpiConfig,
    ILogger<ConsultasBackfillService> logger)
{
    public record BackfillStats(int Scanned, int AppointmentsSet, int ValuesSet, string? Error);

    public async Task<BackfillStats> BackfillUnitAsync(int unitId, CancellationToken ct)
    {
        var unit = await db.Units.AsNoTracking()
            .Where(u => u.Id == unitId)
            .Select(u => new { u.Id, u.ClinicId, u.Name })
            .FirstOrDefaultAsync(ct);
        if (unit is null) return new BackfillStats(0, 0, 0, "unidade não encontrada");

        var profile = await kpiConfig.GetLeadProfileConfigAsync(unitId, ct);
        if (profile.AppointmentFieldId is null && profile.ValorConsultaFieldId is null)
            return new BackfillStats(0, 0, 0,
                "Nenhum campo de Consultas mapeado. Configure 'Data de agendamento' e/ou 'Valor da consulta' em Configurações → Perfil do Lead.");

        // Só leads com CustomFieldsJson preenchido e que ainda precisam de update em
        // pelo menos um dos campos. Status != "deleted" pra não mexer em descartados.
        var leads = await db.Leads
            .Where(l => l.UnitId == unitId
                && l.TenantId == unit.ClinicId
                && l.CustomFieldsJson != null
                && l.Status != "deleted"
                && (l.AppointmentScheduledAt == null || l.ConsultationValue == null))
            .ToListAsync(ct);

        int apptUpdated = 0, valUpdated = 0;
        foreach (var l in leads)
        {
            if (l.AppointmentScheduledAt is null && profile.AppointmentFieldId is not null)
            {
                var date = KommoIngestionService.TryExtractDateFromCustomFields(
                    l.CustomFieldsJson, profile.AppointmentFieldId,
                    n => n.Contains("agendamento"));
                if (date.HasValue)
                {
                    l.AppointmentScheduledAt = date;
                    // Pra dados antigos não temos a data exata em que a SDR preencheu o
                    // campo. Usa Lead.UpdatedAt como proxy (última modificação do lead
                    // — geralmente a mais próxima do preenchimento real).
                    l.AppointmentScheduledAtFilledAt = l.UpdatedAt;
                    apptUpdated++;
                }
            }
            if (l.ConsultationValue is null && profile.ValorConsultaFieldId is not null)
            {
                var val = KommoIngestionService.TryExtractDecimalFromCustomFields(
                    l.CustomFieldsJson, profile.ValorConsultaFieldId,
                    n => n.Contains("valor") && n.Contains("consulta"));
                if (val.HasValue)
                {
                    l.ConsultationValue = val;
                    valUpdated++;
                }
            }
        }

        if (apptUpdated + valUpdated > 0)
            await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "[consultas-backfill] unit={Unit} scanned={N} appts={A} values={V}",
            unitId, leads.Count, apptUpdated, valUpdated);

        return new BackfillStats(leads.Count, apptUpdated, valUpdated, null);
    }
}
