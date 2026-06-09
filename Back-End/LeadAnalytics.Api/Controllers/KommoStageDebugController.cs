using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.Service;
using LeadAnalytics.Api.Service.Stages;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LeadAnalytics.Api.Controllers;

/// <summary>
/// Endpoint TEMPORÁRIO de diagnóstico do fluxo Kommo → KPI "agendados".
/// Mostra em uma chamada o estado da unidade: token funciona, pipelines
/// resolvem por nome, leads com CurrentStage cru vs canônico, e amostras
/// de LeadStageHistory recente. Sem isso ficamos chutando se o KPI dá 0.
///
/// ⚠️ <c>[AllowAnonymous]</c> — remover quando o caso da ITZ estiver fechado.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/admin/kommo-stage-debug")]
public class KommoStageDebugController(
    AppDbContext db,
    KommoApiClient kommoApi,
    ILogger<KommoStageDebugController> logger) : ControllerBase
{
    [HttpGet("by-slug/{slug}")]
    public async Task<IActionResult> ByUnitSlug(string slug, CancellationToken ct)
    {
        var unit = await db.Units.AsNoTracking().FirstOrDefaultAsync(u => u.Slug == slug, ct);
        if (unit is null) return NotFound(new { error = $"unit não encontrada para slug '{slug}'" });
        return await BuildReportAsync(unit.Id, ct);
    }

    [HttpGet("by-id/{unitId:int}")]
    public async Task<IActionResult> ByUnitId(int unitId, CancellationToken ct)
    {
        return await BuildReportAsync(unitId, ct);
    }

    private async Task<IActionResult> BuildReportAsync(int unitId, CancellationToken ct)
    {
        var unit = await db.Units.AsNoTracking().FirstOrDefaultAsync(u => u.Id == unitId, ct);
        if (unit is null) return NotFound(new { error = "unit não encontrada" });

        // 1) Token + pipelines
        object pipelineCheck;
        var stageResolved = new List<object>();
        if (string.IsNullOrWhiteSpace(unit.KommoSubdomain) || string.IsNullOrWhiteSpace(unit.KommoAccessToken))
        {
            pipelineCheck = new { ok = false, reason = "unit sem KommoSubdomain ou KommoAccessToken" };
        }
        else
        {
            try
            {
                var pipes = await kommoApi.GetPipelinesAsync(unit.KommoSubdomain!, unit.KommoAccessToken!, ct);
                var count = 0;
                foreach (var p in pipes?.Embedded?.Pipelines ?? new())
                {
                    foreach (var st in p.Embedded?.Statuses ?? new())
                    {
                        count++;
                        var canonical = CanonicalStages.Resolve(st.Name);
                        stageResolved.Add(new
                        {
                            pipeline = p.Name,
                            status_id = st.Id,
                            name = st.Name,
                            canonical,
                            mapped_to_leadstage = canonical != null ? CanonicalStages.ToLeadStage(canonical) : null,
                        });
                    }
                }
                pipelineCheck = new { ok = true, totalStatuses = count };
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "[stage-debug] erro buscando pipelines unit {Unit}", unitId);
                pipelineCheck = new { ok = false, reason = ex.Message };
            }
        }

        // 2) Leads dessa unit por estado da CurrentStage
        var leadsBase = db.Leads.AsNoTracking()
            .Where(l => l.TenantId == unit.ClinicId && l.UnitId == unit.Id);

        var leadsTotal = await leadsBase.CountAsync(ct);

        var leadsAgendadoCanonical = await leadsBase
            .CountAsync(l => l.CurrentStage == LeadStages.AgendadoSemPagamento
                          || l.CurrentStage == LeadStages.AgendadoComPagamento, ct);

        // Leads cuja CurrentStage é só dígitos (status_id cru — heal não rodou).
        var leadsStageRaw = await leadsBase
            .Where(l => l.CurrentStage != null && l.CurrentStage.Length > 0)
            .Select(l => new { l.Id, l.Name, l.ExternalId, l.CurrentStage, l.CurrentStageId, l.UpdatedAt })
            .ToListAsync(ct);
        var rawList = leadsStageRaw
            .Where(l => !string.IsNullOrEmpty(l.CurrentStage)
                     && l.CurrentStage!.All(char.IsDigit))
            .OrderByDescending(l => l.UpdatedAt)
            .Take(10)
            .ToList();

        var canonicalSample = await leadsBase
            .Where(l => l.CurrentStage == LeadStages.AgendadoSemPagamento
                     || l.CurrentStage == LeadStages.AgendadoComPagamento)
            .OrderByDescending(l => l.UpdatedAt)
            .Take(10)
            .Select(l => new
            {
                l.Id,
                l.Name,
                l.ExternalId,
                l.CurrentStage,
                l.CurrentStageId,
                l.UpdatedAt,
            })
            .ToListAsync(ct);

        // 3) LeadStageHistory recente em etapas de agendado (últimos 14 dias).
        var since = DateTime.UtcNow.AddDays(-14);
        var histRecent = await db.LeadStageHistories.AsNoTracking()
            .Where(h => (h.StageLabel == LeadStages.AgendadoSemPagamento
                      || h.StageLabel == LeadStages.AgendadoComPagamento)
                     && h.ChangedAt >= since
                     && h.Lead.UnitId == unit.Id
                     && h.Lead.TenantId == unit.ClinicId)
            .OrderByDescending(h => h.ChangedAt)
            .Take(20)
            .Select(h => new
            {
                h.LeadId,
                lead_name = h.Lead.Name,
                external_id = h.Lead.ExternalId,
                h.StageLabel,
                h.StageId,
                h.ChangedAt,
            })
            .ToListAsync(ct);

        var histByDay = histRecent
            .GroupBy(h => h.ChangedAt.Date)
            .OrderByDescending(g => g.Key)
            .Select(g => new { day = g.Key.ToString("yyyy-MM-dd"), count = g.Count() })
            .ToList();

        // 4) Para os 2 nomes que o usuário citou — estado do lead.
        var named = await leadsBase
            .Where(l => l.Name != null && (l.Name.StartsWith("Rosa") || l.Name.StartsWith("João De Deus")))
            .Select(l => new
            {
                l.Id,
                l.Name,
                l.ExternalId,
                l.CurrentStage,
                l.CurrentStageId,
                l.UpdatedAt,
                l.CreatedAt,
                LastHistory = l.StageHistory
                    .OrderByDescending(h => h.ChangedAt)
                    .Take(5)
                    .Select(h => new { h.StageLabel, h.StageId, h.ChangedAt })
                    .ToList(),
            })
            .ToListAsync(ct);

        return Ok(new
        {
            unit = new
            {
                unit.Id,
                unit.ClinicId,
                unit.Name,
                unit.Slug,
                unit.KommoSubdomain,
                hasToken = !string.IsNullOrWhiteSpace(unit.KommoAccessToken),
                kommoStageMapJson = unit.KommoStageMapJson,
            },
            pipelineCheck,
            stageResolved,
            leads = new
            {
                total = leadsTotal,
                agendadoCanonical = leadsAgendadoCanonical,
                rawStageCount = rawList.Count,
                rawStageSample = rawList,
                canonicalSample,
            },
            historyLast14d = new
            {
                rowsReturned = histRecent.Count,
                byDay = histByDay,
                rows = histRecent,
            },
            namedLeads = named,
            hint = "AGENDADOS=0 + rawStageCount>0 → heal não rodou (sync não conseguiu pipelines ou não ingeriu). historyLast14d.rows com ChangedAt fora do dia desejado → janela. namedLeads vazio → leads não estão nessa unit.",
        });
    }
}
