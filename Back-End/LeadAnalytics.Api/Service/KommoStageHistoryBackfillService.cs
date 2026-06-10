using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.Models;
using LeadAnalytics.Api.Service.Ai;
using LeadAnalytics.Api.Service.Stages;
using Microsoft.EntityFrameworkCore;

namespace LeadAnalytics.Api.Service;

public record BackfillResult(int EventsScanned, int Inserted, bool HitCap, DateTime? OldestEventUtc, string? Error);

/// <summary>
/// Reconstrói o <see cref="LeadStageHistory"/> com a DATA REAL de entrada em cada etapa,
/// puxando os eventos <c>lead_status_changed</c> da API da Kommo (<see cref="KommoApiClient.GetLeadStatusEventsPageAsync"/>).
///
/// É a "Parte 2" do conserto do KPI "agendados no dia": o sync/heal carimbavam
/// <c>updated_at</c> como data de entrada (linhas marcadas <see cref="LeadStageHistory.SourceLegacy"/>,
/// hoje excluídas das contagens). Este backfill insere linhas <see cref="LeadStageHistory.SourceEventsApi"/>
/// com o <c>created_at</c> verdadeiro do evento — essas SÃO contadas.
///
/// Idempotente: dedup por <see cref="LeadStageHistory.KommoEventId"/> (índice único parcial),
/// então reexecutar não duplica. Ordena por created_at desc e respeita um teto de páginas —
/// se bater o teto, loga (sem corte silencioso).
/// </summary>
public class KommoStageHistoryBackfillService(
    AppDbContext db,
    KommoApiClient api,
    KommoStagesResolver stagesResolver,
    ILogger<KommoStageHistoryBackfillService> logger)
{
    private const int PageSize = 100;          // máximo da API de eventos da Kommo
    private const int InterPageDelayMs = 200;  // respeita rate-limit (7 RPS por conta)

    public async Task<BackfillResult> BackfillUnitAsync(Unit unit, int maxPages, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(unit.KommoSubdomain) || string.IsNullOrWhiteSpace(unit.KommoAccessToken))
            return new BackfillResult(0, 0, false, null, "unidade sem KommoSubdomain/AccessToken");

        // status_id (Kommo) → etapa canônica → label LeadStages.* (o que guardamos em StageLabel).
        var stageNameById = await stagesResolver.GetStageMapAsync(unit.Id, ct);

        // ExternalId (lead id da Kommo) → Id interno do lead, restrito ao tenant/unidade.
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
                resp = await api.GetLeadStatusEventsPageAsync(unit.KommoSubdomain!, unit.KommoAccessToken!, page, PageSize, null, ct);
            }
            catch (HttpRequestException ex)
            {
                return new BackfillResult(scanned, inserted, hitCap, oldest, ex.Message);
            }

            var events = resp?.Embedded?.Events;
            if (events is null || events.Count == 0) break; // 204/fim da paginação

            // Monta as linhas candidatas desta página (mapeando status_id → label canônico).
            var candidates = new List<LeadStageHistory>();
            foreach (var ev in events)
            {
                scanned++;
                var changedAt = DateTimeOffset.FromUnixTimeSeconds(ev.CreatedAt).UtcDateTime;
                if (oldest is null || changedAt < oldest) oldest = changedAt;

                if (ev.EntityId > int.MaxValue) continue;
                if (!leadIdByExternal.TryGetValue((int)ev.EntityId, out var internalLeadId)) continue;

                var statusId = ev.ValueAfter?.FirstOrDefault()?.LeadStatus?.Id;
                if (statusId is not long sid) continue;

                var name = stageNameById.TryGetValue((int)sid, out var n) ? n : null;
                var label = CanonicalStages.ToLeadStage(CanonicalStages.Resolve(name));
                if (label is null) continue; // etapa fora do funil canônico — não rastreamos

                candidates.Add(new LeadStageHistory
                {
                    LeadId = internalLeadId,
                    StageId = (int)sid,
                    StageLabel = label,
                    ChangedAt = changedAt,
                    KommoEventId = ev.Id,
                    EntrySource = LeadStageHistory.SourceEventsApi,
                });
            }

            // Dedup: descarta eventos já gravados (reexecução do backfill). Escopa por
            // (LeadId, KommoEventId) — id de evento é único só dentro da conta/lead.
            if (candidates.Count > 0)
            {
                var ids = candidates.Select(c => c.KommoEventId!.Value).ToList();
                var leadIds = candidates.Select(c => c.LeadId).Distinct().ToList();
                var existing = await db.LeadStageHistories.AsNoTracking()
                    .Where(h => h.KommoEventId != null
                                && leadIds.Contains(h.LeadId)
                                && ids.Contains(h.KommoEventId.Value))
                    .Select(h => new { h.LeadId, EventId = h.KommoEventId!.Value })
                    .ToListAsync(ct);
                var existingSet = existing.Select(x => (x.LeadId, x.EventId)).ToHashSet();

                var fresh = candidates.Where(c => !existingSet.Contains((c.LeadId, c.KommoEventId!.Value))).ToList();
                if (fresh.Count > 0)
                {
                    db.LeadStageHistories.AddRange(fresh);
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
                "[stage-backfill] unit={Unit} bateu o teto de {MaxPages} páginas — eventos anteriores a {Oldest:o} NÃO foram trazidos nesta execução",
                unit.Id, maxPages, oldest);

        return new BackfillResult(scanned, inserted, hitCap, oldest, null);
    }
}
