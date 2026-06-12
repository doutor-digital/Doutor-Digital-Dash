using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.Models;
using LeadAnalytics.Api.Service;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LeadAnalytics.Api.Controllers;

/// <summary>
/// Auditoria de movimentações de etapa: admin lista as transições do período pra
/// revisar erros das SDRs (moveu o lead no dia errado, esqueceu de mover, etc.)
/// e pode corrigir a data ou marcar como "não contar" no KPI correspondente.
///
/// Endpoints (todos admin-only):
///  • GET    /api/admin/stage-history/audit?unitId=&amp;dateFrom=&amp;dateTo=&amp;kpiKey=&amp;leadName=
///  • PATCH  /api/admin/stage-history/{id}/corrected-date  body { corrected_at, reason? }
///  • DELETE /api/admin/stage-history/{id}/corrected-date  (volta pro original)
/// </summary>
[ApiController]
[Authorize]
[Route("api/admin/stage-history")]
public class AdminStageHistoryController(
    AppDbContext db,
    TenantUnitGuard tenantGuard,
    ICurrentUser currentUser,
    ILogger<AdminStageHistoryController> logger) : ControllerBase
{
    public record CorrectDateBody(DateTime CorrectedAt, string? Reason);

    /// <summary>Mapeia cada stage_label canônica para o KPI key correspondente.</summary>
    private static string? KpiKeyForStage(string stageLabel) => stageLabel switch
    {
        LeadStages.AgendadoSemPagamento => "agendados",
        LeadStages.AgendadoComPagamento => "agendados",
        LeadStages.Faltou => "no_show",
        LeadStages.FechouTratamento => "tratamentos",
        LeadStages.EmTratamento => "tratamentos",
        LeadStages.NaoFechouTratamento => "tratamentos",
        _ => null,
    };

    private IActionResult? RequireAdmin() =>
        currentUser.IsAdminLevel ? null
            : StatusCode(403, new { error = "acesso restrito ao admin" });

    [HttpGet("audit")]
    public async Task<IActionResult> Audit(
        [FromQuery] int unitId,
        [FromQuery] DateTime dateFrom,
        [FromQuery] DateTime dateTo,
        [FromQuery] string? kpiKey,
        [FromQuery] string? leadName,
        [FromQuery] int limit = 500,
        CancellationToken ct = default)
    {
        if (RequireAdmin() is { } denied) return denied;
        if (await tenantGuard.EnsureUnitBelongsToTenantAsync(unitId, ct) is { } guard) return guard;

        var unit = await db.Units.AsNoTracking()
            .Where(u => u.Id == unitId)
            .Select(u => new { u.Id, u.ClinicId })
            .FirstOrDefaultAsync(ct);
        if (unit is null) return NotFound(new { error = "unit não encontrada" });

        var from = AsUtc(dateFrom);
        var to = AsUtc(dateTo);
        if (to < from) return BadRequest(new { error = "dateTo deve ser >= dateFrom" });

        // Filtro por KPI: traduz pra stage_labels relevantes.
        string[]? wantedStages = kpiKey switch
        {
            "agendados" => new[] { LeadStages.AgendadoSemPagamento, LeadStages.AgendadoComPagamento },
            "no_show" => new[] { LeadStages.Faltou },
            "tratamentos" => new[] { LeadStages.FechouTratamento, LeadStages.EmTratamento, LeadStages.NaoFechouTratamento },
            _ => null,
        };

        var q = db.LeadStageHistories.AsNoTracking()
            .Where(h => h.Lead!.TenantId == unit.ClinicId
                && h.Lead.UnitId == unitId
                && h.EntrySource != LeadStageHistory.SourceLegacy
                && (h.CorrectedChangedAt ?? h.ChangedAt) >= from
                && (h.CorrectedChangedAt ?? h.ChangedAt) <= to);
        if (wantedStages is not null)
            q = q.Where(h => wantedStages.Contains(h.StageLabel));
        if (!string.IsNullOrWhiteSpace(leadName))
            q = q.Where(h => EF.Functions.ILike(h.Lead!.Name, $"%{leadName.Trim()}%"));

        var raw = await q
            .OrderByDescending(h => (h.CorrectedChangedAt ?? h.ChangedAt))
            .Take(limit)
            .Select(h => new {
                h.Id,
                h.LeadId,
                LeadName = h.Lead!.Name,
                h.StageId,
                h.StageLabel,
                h.ChangedAt,
                h.CorrectedChangedAt,
                h.CorrectedAt,
                h.CorrectedByEmail,
                h.CorrectionReason,
                h.EntrySource,
            })
            .ToListAsync(ct);

        // Marca quais leads estão em kpi_exclusions (por KPI). Carrega uma vez e cruza.
        var leadIds = raw.Select(x => x.LeadId).Distinct().ToList();
        var excluded = await db.KpiExclusions.AsNoTracking()
            .Where(e => e.TenantId == unit.ClinicId && e.UnitId == unitId
                && leadIds.Contains(e.LeadId))
            .Select(e => new { e.LeadId, e.KpiKey })
            .ToListAsync(ct);
        var excludedSet = excluded
            .Select(e => (e.LeadId, e.KpiKey))
            .ToHashSet();

        var items = raw.Select(h => new {
            id = h.Id,
            lead_id = h.LeadId,
            lead_name = h.LeadName,
            stage_id = h.StageId,
            stage_label = h.StageLabel,
            kpi_key = KpiKeyForStage(h.StageLabel),
            original_changed_at = h.ChangedAt,
            corrected_changed_at = h.CorrectedChangedAt,
            effective_changed_at = h.CorrectedChangedAt ?? h.ChangedAt,
            corrected_at = h.CorrectedAt,
            corrected_by_email = h.CorrectedByEmail,
            correction_reason = h.CorrectionReason,
            entry_source = h.EntrySource,
            excluded = KpiKeyForStage(h.StageLabel) is string k
                && excludedSet.Contains((h.LeadId, k)),
        }).ToList();

        return Ok(new { items, total = items.Count, truncated = items.Count >= limit });
    }

    /// <summary>Aplica uma data corrigida na transição (mantém ChangedAt original na trilha).</summary>
    [HttpPatch("{id:int}/corrected-date")]
    public async Task<IActionResult> CorrectDate(
        int id,
        [FromBody] CorrectDateBody body,
        CancellationToken ct = default)
    {
        if (RequireAdmin() is { } denied) return denied;

        var row = await db.LeadStageHistories
            .Include(h => h.Lead)
            .FirstOrDefaultAsync(h => h.Id == id, ct);
        if (row is null) return NotFound(new { error = "transição não encontrada" });

        // Garante que pertence ao tenant do admin (anti cross-tenant).
        if (currentUser.TenantId is int t && row.Lead!.TenantId != t)
            return StatusCode(403, new { error = "transição fora do seu tenant" });

        row.CorrectedChangedAt = AsUtc(body.CorrectedAt);
        row.CorrectedAt = DateTime.UtcNow;
        row.CorrectedByUserId = currentUser.UserId;
        row.CorrectedByEmail = currentUser.Email;
        row.CorrectionReason = string.IsNullOrWhiteSpace(body.Reason) ? null : body.Reason.Trim();
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "[stage-history] correct id={Id} lead={Lead} orig={Orig} corrected={Corrected} by={By}",
            id, row.LeadId, row.ChangedAt, row.CorrectedChangedAt, currentUser.Email);
        return Ok(new {
            ok = true,
            id = row.Id,
            corrected_changed_at = row.CorrectedChangedAt,
            corrected_by_email = row.CorrectedByEmail,
        });
    }

    /// <summary>Remove a correção, voltando o KPI a contar a transição na data original.</summary>
    [HttpDelete("{id:int}/corrected-date")]
    public async Task<IActionResult> ResetCorrection(int id, CancellationToken ct = default)
    {
        if (RequireAdmin() is { } denied) return denied;

        var row = await db.LeadStageHistories
            .Include(h => h.Lead)
            .FirstOrDefaultAsync(h => h.Id == id, ct);
        if (row is null) return NotFound(new { error = "transição não encontrada" });
        if (currentUser.TenantId is int t && row.Lead!.TenantId != t)
            return StatusCode(403, new { error = "transição fora do seu tenant" });

        row.CorrectedChangedAt = null;
        row.CorrectedAt = null;
        row.CorrectedByUserId = null;
        row.CorrectedByEmail = null;
        row.CorrectionReason = null;
        await db.SaveChangesAsync(ct);

        logger.LogInformation("[stage-history] reset id={Id} lead={Lead} by={By}",
            id, row.LeadId, currentUser.Email);
        return Ok(new { ok = true });
    }

    private static DateTime AsUtc(DateTime d) =>
        d.Kind == DateTimeKind.Utc ? d
        : d.Kind == DateTimeKind.Local ? d.ToUniversalTime()
        : DateTime.SpecifyKind(d, DateTimeKind.Utc);
}
