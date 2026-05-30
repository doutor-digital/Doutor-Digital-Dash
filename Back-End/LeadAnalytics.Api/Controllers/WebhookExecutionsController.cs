using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.DTOs.Webhooks;
using LeadAnalytics.Api.Service;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LeadAnalytics.Api.Controllers;

/// <summary>
/// API que alimenta o painel <c>/webhooks-monitor</c> no front. Lista as
/// execuções recentes de webhooks (todas providers, mas hoje só Kommo),
/// com filtros por unidade, status e janela de data + KPIs do topo.
/// </summary>
[ApiController]
[Authorize]
[Route("api/webhooks/executions")]
public class WebhookExecutionsController(
    AppDbContext db,
    TenantUnitGuard tenantGuard) : ControllerBase
{
    private readonly AppDbContext _db = db;
    private readonly TenantUnitGuard _tenantGuard = tenantGuard;

    /// <summary>Listagem paginada com filtros.</summary>
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] int? unitId,
        [FromQuery] string? status,           // success | failed | ignored
        [FromQuery] string? provider,
        [FromQuery] DateTime? dateFrom,
        [FromQuery] DateTime? dateTo,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        if (page < 1) page = 1;
        if (pageSize is < 1 or > 200) pageSize = 50;

        var (error, tenantId) = await _tenantGuard.ResolveTenantAsync(unitId, ct);
        if (error is not null) return error;

        var q = _db.WebhookExecutions.AsNoTracking().AsQueryable();

        if (tenantId.HasValue)
            q = q.Where(e => e.TenantId == tenantId);
        if (unitId.HasValue)
            q = q.Where(e => e.UnitId == unitId);
        if (!string.IsNullOrWhiteSpace(status))
            q = q.Where(e => e.Status == status);
        if (!string.IsNullOrWhiteSpace(provider))
            q = q.Where(e => e.Provider == provider);
        if (dateFrom.HasValue)
            q = q.Where(e => e.ReceivedAt >= dateFrom.Value);
        if (dateTo.HasValue)
            q = q.Where(e => e.ReceivedAt <= dateTo.Value);

        var total = await q.CountAsync(ct);

        var items = await q
            .OrderByDescending(e => e.ReceivedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Join(_db.Units.AsNoTracking(),
                  e => e.UnitId,
                  u => u.Id,
                  (e, u) => new WebhookExecutionSummaryDto
                  {
                      Id = e.Id,
                      Provider = e.Provider,
                      Slug = e.Slug,
                      UnitId = e.UnitId,
                      UnitName = u.Name,
                      TenantId = e.TenantId,
                      KommoSubdomain = e.KommoSubdomain,
                      ReceivedAt = e.ReceivedAt,
                      DurationMs = e.DurationMs,
                      Status = e.Status,
                      StatusCode = e.StatusCode,
                      Success = e.Success,
                      ErrorMessage = e.ErrorMessage,
                      EventsParsed = e.EventsParsed,
                      LeadsPersisted = e.LeadsPersisted,
                      FormKeys = e.FormKeys,
                      Ip = e.Ip,
                  })
            .ToListAsync(ct);

        // Inclui registros sem UnitId (slug errado etc.) — esses não entram no Join acima.
        if (!unitId.HasValue && tenantId is null)
        {
            var orphans = await q
                .Where(e => e.UnitId == null)
                .OrderByDescending(e => e.ReceivedAt)
                .Take(pageSize)
                .Select(e => new WebhookExecutionSummaryDto
                {
                    Id = e.Id,
                    Provider = e.Provider,
                    Slug = e.Slug,
                    UnitId = null,
                    UnitName = null,
                    TenantId = e.TenantId,
                    KommoSubdomain = e.KommoSubdomain,
                    ReceivedAt = e.ReceivedAt,
                    DurationMs = e.DurationMs,
                    Status = e.Status,
                    StatusCode = e.StatusCode,
                    Success = e.Success,
                    ErrorMessage = e.ErrorMessage,
                    EventsParsed = e.EventsParsed,
                    LeadsPersisted = e.LeadsPersisted,
                    FormKeys = e.FormKeys,
                    Ip = e.Ip,
                })
                .ToListAsync(ct);

            items = items.Concat(orphans)
                .OrderByDescending(x => x.ReceivedAt)
                .Take(pageSize)
                .ToList();
        }

        return Ok(new WebhookExecutionListDto
        {
            Items = items,
            Total = total,
            Page = page,
            PageSize = pageSize,
        });
    }

    /// <summary>KPIs do topo do painel.</summary>
    [HttpGet("stats")]
    public async Task<IActionResult> Stats(
        [FromQuery] int? unitId,
        [FromQuery] DateTime? dateFrom,
        [FromQuery] DateTime? dateTo,
        CancellationToken ct = default)
    {
        var (error, tenantId) = await _tenantGuard.ResolveTenantAsync(unitId, ct);
        if (error is not null) return error;

        var q = _db.WebhookExecutions.AsNoTracking().AsQueryable();
        if (tenantId.HasValue) q = q.Where(e => e.TenantId == tenantId);
        if (unitId.HasValue) q = q.Where(e => e.UnitId == unitId);
        if (dateFrom.HasValue) q = q.Where(e => e.ReceivedAt >= dateFrom.Value);
        if (dateTo.HasValue) q = q.Where(e => e.ReceivedAt <= dateTo.Value);

        var total = await q.CountAsync(ct);
        if (total == 0)
            return Ok(new WebhookExecutionStatsDto());

        var success = await q.CountAsync(e => e.Status == "success", ct);
        var failed = await q.CountAsync(e => e.Status == "failed", ct);
        var ignored = await q.CountAsync(e => e.Status == "ignored", ct);
        var leads = await q.SumAsync(e => (int?)e.LeadsPersisted, ct) ?? 0;
        var avgMs = await q.AverageAsync(e => (double?)e.DurationMs, ct);
        var lastFail = await q.Where(e => e.Status == "failed").MaxAsync(e => (DateTime?)e.ReceivedAt, ct);
        var lastOk = await q.Where(e => e.Status == "success").MaxAsync(e => (DateTime?)e.ReceivedAt, ct);

        return Ok(new WebhookExecutionStatsDto
        {
            Total = total,
            Success = success,
            Failed = failed,
            Ignored = ignored,
            LeadsPersisted = leads,
            AvgDurationMs = (int)Math.Round(avgMs ?? 0),
            LastFailureAt = lastFail,
            LastSuccessAt = lastOk,
        });
    }

    /// <summary>Detalhe completo de uma execução (com payload bruto e response).</summary>
    [HttpGet("{id:long}")]
    public async Task<IActionResult> GetById(long id, CancellationToken ct)
    {
        if (_tenantGuard.RequireTenant(out var tenantId) is { } denied) return denied;

        var q = _db.WebhookExecutions.AsNoTracking().Where(e => e.Id == id);
        if (tenantId.HasValue)
            q = q.Where(e => e.TenantId == tenantId || e.TenantId == null);

        var dto = await (from e in q
                         join u in _db.Units.AsNoTracking() on e.UnitId equals u.Id into ujoin
                         from u in ujoin.DefaultIfEmpty()
                         select new WebhookExecutionDetailDto
                         {
                             Id = e.Id,
                             Provider = e.Provider,
                             Slug = e.Slug,
                             UnitId = e.UnitId,
                             UnitName = u != null ? u.Name : null,
                             TenantId = e.TenantId,
                             KommoSubdomain = e.KommoSubdomain,
                             KommoAccountId = e.KommoAccountId,
                             ReceivedAt = e.ReceivedAt,
                             DurationMs = e.DurationMs,
                             Status = e.Status,
                             StatusCode = e.StatusCode,
                             Success = e.Success,
                             ErrorMessage = e.ErrorMessage,
                             ErrorStack = e.ErrorStack,
                             EventsParsed = e.EventsParsed,
                             EventsSummary = e.EventsSummary,
                             LeadsPersisted = e.LeadsPersisted,
                             FormKeys = e.FormKeys,
                             FormKeyCount = e.FormKeyCount,
                             Ip = e.Ip,
                             Method = e.Method,
                             Path = e.Path,
                             UserAgent = e.UserAgent,
                             ContentType = e.ContentType,
                             ContentLength = e.ContentLength,
                             RawPayload = e.RawPayload,
                             PayloadTruncated = e.PayloadTruncated,
                             ResponseBody = e.ResponseBody,
                         })
                        .FirstOrDefaultAsync(ct);

        return dto is null ? NotFound() : Ok(dto);
    }
}
