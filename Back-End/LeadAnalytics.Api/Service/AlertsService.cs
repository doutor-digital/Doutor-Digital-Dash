using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.DTOs.Alerts;
using Microsoft.EntityFrameworkCore;

namespace LeadAnalytics.Api.Service;

/// <summary>
/// Lógica dos alertas operacionais, antes em BackgroundServices (jobs).
/// Agora o "quando" (cron) e o "avisar alguém" (notificação) vivem no n8n;
/// o .NET só expõe a detecção + a transição de estado. Ver AlertsController.
/// </summary>
public class AlertsService(AppDbContext db, ILogger<AlertsService> logger)
{
    private readonly AppDbContext _db = db;
    private readonly ILogger<AlertsService> _logger = logger;

    /// <summary>
    /// Marca parcelas "pendente" com vencimento passado como "atrasado" e
    /// devolve o que mudou (para o n8n notificar). Idempotente por dia: uma
    /// parcela já marcada "atrasado" não reaparece.
    /// </summary>
    public async Task<List<OverdueInstallmentDto>> RunOverdueInstallmentsAsync(CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var overdue = await _db.TreatmentInstallments
            .Where(i => i.Status == "pendente"
                     && i.DueDate != null
                     && i.DueDate < today)
            .ToListAsync(ct);

        if (overdue.Count == 0) return [];

        foreach (var i in overdue)
            i.Status = "atrasado";

        await _db.SaveChangesAsync(ct);

        // Contexto (tenant/unit/lead) vem do Treatment pai — a parcela não guarda.
        var treatmentIds = overdue.Select(i => i.TreatmentId).Distinct().ToList();
        var ctx = await _db.Treatments
            .AsNoTracking()
            .Where(t => treatmentIds.Contains(t.Id))
            .Select(t => new
            {
                t.Id,
                t.LeadId,
                t.TenantId,
                t.UnitId,
                LeadName = t.Lead != null ? t.Lead.Name : null,
                LeadPhone = t.Lead != null ? t.Lead.Phone : null
            })
            .ToDictionaryAsync(t => t.Id, ct);

        _logger.LogInformation("💰 {Count} parcela(s) marcadas como atrasadas", overdue.Count);

        return overdue.Select(i =>
        {
            ctx.TryGetValue(i.TreatmentId, out var t);
            return new OverdueInstallmentDto
            {
                InstallmentId = i.Id,
                TreatmentId = i.TreatmentId,
                LeadId = t?.LeadId ?? 0,
                LeadName = t?.LeadName,
                LeadPhone = t?.LeadPhone,
                TenantId = t?.TenantId ?? 0,
                UnitId = t?.UnitId,
                Sequence = i.Sequence,
                Amount = i.Amount,
                PaymentMethod = i.PaymentMethod,
                DueDate = i.DueDate,
                DaysOverdue = i.DueDate is null ? 0 : today.DayNumber - i.DueDate.Value.DayNumber
            };
        }).ToList();
    }

    /// <summary>
    /// Tratamentos em "aguardando_dados" há mais que <paramref name="threshold"/>
    /// sem preenchimento da SDR. Read-only — não muda nada no banco.
    /// </summary>
    public async Task<List<PendingFillDto>> GetPendingFillsAsync(TimeSpan threshold, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var cutoff = now - threshold;

        var pending = await _db.Treatments
            .AsNoTracking()
            .Where(t => t.Status == "aguardando_dados" && t.CreatedAt < cutoff)
            .Select(t => new PendingFillDto
            {
                TreatmentId = t.Id,
                LeadId = t.LeadId,
                LeadName = t.Lead != null ? t.Lead.Name : null,
                LeadPhone = t.Lead != null ? t.Lead.Phone : null,
                TenantId = t.TenantId,
                UnitId = t.UnitId,
                CreatedAt = t.CreatedAt
            })
            .ToListAsync(ct);

        foreach (var p in pending)
            p.HoursPending = Math.Round((now - p.CreatedAt).TotalHours, 1);

        return pending;
    }
}
