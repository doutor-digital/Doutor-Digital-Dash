using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace LeadAnalytics.Api.Service;

/// <summary>
/// Reconstrói as tentativas de resgate com a DATA REAL do preenchimento, puxando os eventos
/// de mudança do custom field "Tentativas de resgastes" da Kommo
/// (<see cref="KommoApiClient.GetCustomFieldChangeEventsPageAsync"/>).
///
/// Resgate = lead VELHO sendo recuperado, então contar por data de criação do lead perde a
/// maioria. A data certa ("resgatei no dia X") é a do evento de mudança do campo. Grava em
/// <see cref="RecoveryAttempt"/> (Method="resgate", Outcome=valor da tentativa, CreatedAt=data
/// do evento), idempotente por (LeadId, KommoEventId).
/// </summary>
public class ResgateAttemptBackfillService(
    AppDbContext db,
    KommoApiClient api,
    ILogger<ResgateAttemptBackfillService> logger)
{
    private const int PageSize = 100;
    private const int InterPageDelayMs = 200;

    public async Task<BackfillResult> BackfillUnitAsync(Unit unit, int maxPages, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(unit.KommoSubdomain) || string.IsNullOrWhiteSpace(unit.KommoAccessToken))
            return new BackfillResult(0, 0, false, null, "unidade sem KommoSubdomain/AccessToken");

        // 1) Resolve o id do campo "Tentativas de resgastes" pelo NOME (varia por conta).
        long fieldId;
        try
        {
            var fields = await api.GetCustomFieldsAsync(unit.KommoSubdomain!, unit.KommoAccessToken!, ct);
            // O nome do campo na Kommo é "Tentativas de resgastes" (typo do cliente:
            // "resgaSTes" em vez de "resgaTes"). Procurar "resgat" não casa porque
            // entre 'a' e 't' tem um 'S'. Usa "resga" pra cobrir as duas grafias.
            var f = fields?.Embedded?.CustomFields?.FirstOrDefault(x =>
            {
                var n = (x.Name ?? string.Empty).ToLowerInvariant();
                return n.Contains("tentativ") && n.Contains("resga");
            });
            if (f is null) return new BackfillResult(0, 0, false, null, "campo 'Tentativas de resgastes' não encontrado");
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

        var scanned = 0;
        var inserted = 0;
        var hitCap = false;
        DateTime? oldest = null;

        for (var page = 1; page <= maxPages; page++)
        {
            if (ct.IsCancellationRequested) break;

            KommoEventsPageResponse? resp;
            try
            {
                resp = await api.GetCustomFieldChangeEventsPageAsync(unit.KommoSubdomain!, unit.KommoAccessToken!, fieldId, page, PageSize, ct);
            }
            catch (HttpRequestException ex)
            {
                return new BackfillResult(scanned, inserted, hitCap, oldest, ex.Message);
            }

            var events = resp?.Embedded?.Events;
            if (events is null || events.Count == 0) break;

            var candidates = new List<RecoveryAttempt>();
            foreach (var ev in events)
            {
                scanned++;
                var changedAt = DateTimeOffset.FromUnixTimeSeconds(ev.CreatedAt).UtcDateTime;
                if (oldest is null || changedAt < oldest) oldest = changedAt;

                if (string.IsNullOrEmpty(ev.Id)) continue;
                if (ev.EntityId > int.MaxValue) continue;
                if (!leadIdByExternal.TryGetValue((int)ev.EntityId, out var internalLeadId)) continue;

                // Valor preenchido (ex.: "Resgaste 1 - 24h"). Ignora limpeza (value_after vazio).
                var text = ev.ValueAfter?.FirstOrDefault()?.CustomFieldValue?.Text?.Trim();
                if (string.IsNullOrWhiteSpace(text)) continue;

                candidates.Add(new RecoveryAttempt
                {
                    LeadId = internalLeadId,
                    TenantId = unit.ClinicId,
                    Method = "resgate",
                    Outcome = text!,
                    CreatedAt = changedAt,
                    KommoEventId = ev.Id,
                    EntrySource = "events_api",
                });
            }

            if (candidates.Count > 0)
            {
                var ids = candidates.Select(c => c.KommoEventId!).ToList();
                var leadIds = candidates.Select(c => c.LeadId).Distinct().ToList();
                var existing = await db.RecoveryAttempts.AsNoTracking()
                    .Where(r => r.KommoEventId != null && leadIds.Contains(r.LeadId) && ids.Contains(r.KommoEventId))
                    .Select(r => new { r.LeadId, EventId = r.KommoEventId! })
                    .ToListAsync(ct);
                var existingSet = existing.Select(x => (x.LeadId, x.EventId)).ToHashSet();

                var fresh = candidates.Where(c => !existingSet.Contains((c.LeadId, c.KommoEventId!))).ToList();
                if (fresh.Count > 0)
                {
                    db.RecoveryAttempts.AddRange(fresh);
                    await db.SaveChangesAsync(ct);
                    inserted += fresh.Count;
                }
            }

            if (page == maxPages && events.Count == PageSize) hitCap = true;

            try { await Task.Delay(InterPageDelayMs, ct); }
            catch (OperationCanceledException) { break; }
        }

        if (hitCap)
            logger.LogWarning(
                "[resgate-backfill] unit={Unit} bateu o teto de {MaxPages} páginas — eventos antes de {Oldest:o} não vieram nesta execução",
                unit.Id, maxPages, oldest);

        return new BackfillResult(scanned, inserted, hitCap, oldest, null);
    }
}
