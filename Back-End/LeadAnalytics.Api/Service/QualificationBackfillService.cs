using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace LeadAnalytics.Api.Service;

/// <summary>
/// Corrige a DATA REAL de preenchimento do campo "Qualificação do lead" puxando os eventos
/// <c>custom_field_{id}_value_changed</c> da Kommo (<see cref="KommoApiClient.GetCustomFieldChangeEventsPageAsync"/>).
///
/// O widget "Qualificação dos Leads" conta por <see cref="Lead.QualificationFilledAt"/> — a
/// produtividade do dia em que a SDR qualifica. O webhook ao vivo já carimba a data em tempo
/// real (<see cref="KommoIngestionService"/>); este backfill repõe o que veio ANTES do deploy:
/// quando a SDR qualifica um lead ANTIGO, a data certa é a do evento, não a criação do lead.
///
/// Idempotente: a Kommo devolve os eventos em ordem DECRESCENTE de <c>created_at</c>, então o
/// PRIMEIRO evento visto por lead é o mais recente — é esse que vira o valor/data em vigor.
/// </summary>
public class QualificationBackfillService(
    AppDbContext db,
    KommoApiClient api,
    ILogger<QualificationBackfillService> logger)
{
    private const int PageSize = 100;
    private const int InterPageDelayMs = 200;

    public async Task<BackfillResult> BackfillUnitAsync(Unit unit, int maxPages, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(unit.KommoSubdomain) || string.IsNullOrWhiteSpace(unit.KommoAccessToken))
            return new BackfillResult(0, 0, false, null, "unidade sem KommoSubdomain/AccessToken");

        // 1) Resolve o id do campo "Qualificação do lead" pelo NOME (varia por conta Kommo).
        long fieldId;
        try
        {
            var fields = await api.GetCustomFieldsAsync(unit.KommoSubdomain!, unit.KommoAccessToken!, ct);
            var f = fields?.Embedded?.CustomFields?.FirstOrDefault(x =>
                (x.Name ?? string.Empty).ToLowerInvariant().Contains("qualifica"));
            if (f is null) return new BackfillResult(0, 0, false, null, "campo 'Qualificação do lead' não encontrado");
            fieldId = f.Id;
        }
        catch (HttpRequestException ex)
        {
            return new BackfillResult(0, 0, false, null, ex.Message);
        }

        // 2) ExternalId (lead da Kommo) → Id interno, restrito ao tenant/unidade.
        var leadIdByExternal = await db.Leads.AsNoTracking()
            .Where(l => l.TenantId == unit.ClinicId && l.UnitId == unit.Id)
            .Select(l => new { l.Id, l.ExternalId })
            .ToDictionaryAsync(x => x.ExternalId, x => x.Id, ct);

        // Evento MAIS RECENTE por lead: internalLeadId → (valor preenchido, data do evento).
        var latest = new Dictionary<int, (string Val, DateTime At)>();
        var scanned = 0;
        var hitCap = false;
        DateTime? oldest = null;

        for (var page = 1; page <= maxPages; page++)
        {
            if (ct.IsCancellationRequested) break;

            KommoEventsPageResponse? resp;
            try
            {
                resp = await api.GetCustomFieldChangeEventsPageAsync(
                    unit.KommoSubdomain!, unit.KommoAccessToken!, fieldId, page, PageSize, ct);
            }
            catch (HttpRequestException ex)
            {
                return new BackfillResult(scanned, latest.Count, hitCap, oldest, ex.Message);
            }

            var events = resp?.Embedded?.Events;
            if (events is null || events.Count == 0) break;

            foreach (var ev in events)
            {
                scanned++;
                var changedAt = DateTimeOffset.FromUnixTimeSeconds(ev.CreatedAt).UtcDateTime;
                if (oldest is null || changedAt < oldest) oldest = changedAt;

                if (ev.EntityId > int.MaxValue) continue;
                if (!leadIdByExternal.TryGetValue((int)ev.EntityId, out var internalLeadId)) continue;
                if (latest.ContainsKey(internalLeadId)) continue; // já pegou o evento mais recente deste lead

                // Valor preenchido (ex.: "Quente"). Ignora limpeza do campo (value_after vazio).
                var text = ev.ValueAfter?.FirstOrDefault()?.CustomFieldValue?.Text?.Trim();
                if (string.IsNullOrWhiteSpace(text)) continue;

                latest[internalLeadId] = (text!, changedAt);
            }

            if (page == maxPages && events.Count == PageSize) hitCap = true;

            try { await Task.Delay(InterPageDelayMs, ct); }
            catch (OperationCanceledException) { break; }
        }

        // 3) Grava a data/valor REAL do evento mais recente em cada lead.
        var updated = 0;
        if (latest.Count > 0)
        {
            var ids = latest.Keys.ToList();
            var leads = await db.Leads.Where(l => ids.Contains(l.Id)).ToListAsync(ct);
            foreach (var lead in leads)
            {
                var (val, at) = latest[lead.Id];
                if (lead.Qualification == val && lead.QualificationFilledAt == at) continue;
                lead.Qualification = val;
                lead.QualificationFilledAt = at;
                updated++;
            }
            if (updated > 0) await db.SaveChangesAsync(ct);
        }

        if (hitCap)
            logger.LogWarning(
                "[qualif-backfill] unit={Unit} bateu o teto de {MaxPages} páginas — eventos antes de {Oldest:o} não vieram nesta execução",
                unit.Id, maxPages, oldest);

        return new BackfillResult(scanned, updated, hitCap, oldest, null);
    }
}
