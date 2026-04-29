using LeadAnalytics.Api.Service;
using LeadAnalytics.Api.Service.Insights;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LeadAnalytics.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/insights")]
public class InsightsController(
    InsightsService insights,
    MetaCapiService capi,
    TenantUnitGuard tenantGuard,
    ILogger<InsightsController> logger) : ControllerBase
{
    private const int MaxPeriodDays = 366;

    private async Task<(IActionResult? Error, int? TenantId)> ResolveAsync(int? unitId)
        => await tenantGuard.ResolveTenantAsync(unitId, HttpContext.RequestAborted);

    private static IActionResult? ValidatePeriod(DateTime? start, DateTime? end)
    {
        if (start.HasValue && end.HasValue && end.Value < start.Value)
            return new BadRequestObjectResult(new ProblemDetails
            { Title = "endDate deve ser maior ou igual a startDate", Status = 400 });

        if (start.HasValue && end.HasValue
            && (end.Value - start.Value).TotalDays > MaxPeriodDays)
            return new BadRequestObjectResult(new ProblemDetails
            { Title = $"Intervalo máximo é {MaxPeriodDays} dias", Status = 400 });

        return null;
    }

    // ─── CAPI EVENTS ─────────────────────────────────────────────────────────

    /// <summary>Lista eventos enviados (mockados) para a Meta Conversions API.</summary>
    [HttpGet("capi/events")]
    public async Task<IActionResult> ListCapiEvents(
        [FromQuery] int? unitId,
        [FromQuery] DateTime? startDate,
        [FromQuery] DateTime? endDate,
        [FromQuery] string? eventName,
        [FromQuery] string? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        if (ValidatePeriod(startDate, endDate) is { } v) return v;
        var (err, tenantId) = await ResolveAsync(unitId);
        if (err is not null) return err;

        var data = await capi.ListAsync(tenantId, unitId, startDate, endDate,
            eventName, status, page, pageSize, HttpContext.RequestAborted);
        return Ok(data);
    }

    [HttpGet("capi/events/{id}")]
    public async Task<IActionResult> GetCapiEvent(string id)
    {
        if (tenantGuard.RequireTenant(out var tenantId) is { } err) return err;
        var ev = await capi.GetAsync(id, tenantId, HttpContext.RequestAborted);
        return ev is null ? NotFound() : Ok(ev);
    }

    /// <summary>Reenvia um evento mockado (status passa para 'sent').</summary>
    [HttpPost("capi/events/{id}/retry")]
    public async Task<IActionResult> RetryCapiEvent(string id)
    {
        if (tenantGuard.RequireTenant(out var tenantId) is { } err) return err;
        var ev = await capi.RetryAsync(id, tenantId, HttpContext.RequestAborted);
        if (ev is null) return NotFound();
        logger.LogInformation("CAPI retry mockado para evento {Id}", id);
        return Ok(ev);
    }

    /// <summary>Saúde do pixel (cobertura email/phone/IP/fbp/fbc + EMQ).</summary>
    [HttpGet("capi/pixel-health")]
    public async Task<IActionResult> PixelHealth(
        [FromQuery] int? unitId,
        [FromQuery] DateTime? startDate,
        [FromQuery] DateTime? endDate)
    {
        if (ValidatePeriod(startDate, endDate) is { } v) return v;
        var (err, tenantId) = await ResolveAsync(unitId);
        if (err is not null) return err;

        var data = await capi.GetPixelHealthAsync(tenantId, unitId, startDate, endDate,
            HttpContext.RequestAborted);
        return Ok(data);
    }

    // ─── ATTRIBUTION ─────────────────────────────────────────────────────────

    [HttpGet("attribution/leads/{leadId}/path")]
    public async Task<IActionResult> AttributionPath(int leadId)
    {
        if (tenantGuard.RequireTenant(out var tenantId) is { } err) return err;
        var data = await insights.GetLeadAttributionPathAsync(leadId, tenantId,
            HttpContext.RequestAborted);
        return data is null ? NotFound() : Ok(data);
    }

    [HttpGet("attribution/summary")]
    public async Task<IActionResult> AttributionSummary(
        [FromQuery] int? unitId,
        [FromQuery] DateTime? startDate,
        [FromQuery] DateTime? endDate)
    {
        if (ValidatePeriod(startDate, endDate) is { } v) return v;
        var (err, tenantId) = await ResolveAsync(unitId);
        if (err is not null) return err;
        return Ok(await insights.GetAttributionSummaryAsync(tenantId, unitId,
            startDate, endDate, HttpContext.RequestAborted));
    }

    // ─── UTM ─────────────────────────────────────────────────────────────────

    [HttpGet("utm")]
    public async Task<IActionResult> Utm(
        [FromQuery] int? unitId,
        [FromQuery] DateTime? startDate,
        [FromQuery] DateTime? endDate)
    {
        if (ValidatePeriod(startDate, endDate) is { } v) return v;
        var (err, tenantId) = await ResolveAsync(unitId);
        if (err is not null) return err;
        return Ok(await insights.GetUtmExplorerAsync(tenantId, unitId,
            startDate, endDate, HttpContext.RequestAborted));
    }

    // ─── SLA ─────────────────────────────────────────────────────────────────

    [HttpGet("sla")]
    public async Task<IActionResult> Sla(
        [FromQuery] int? unitId,
        [FromQuery] DateTime? startDate,
        [FromQuery] DateTime? endDate,
        [FromQuery] int targetMinutes = 5)
    {
        if (ValidatePeriod(startDate, endDate) is { } v) return v;
        var (err, tenantId) = await ResolveAsync(unitId);
        if (err is not null) return err;
        return Ok(await insights.GetSlaAsync(tenantId, unitId,
            startDate, endDate, targetMinutes, HttpContext.RequestAborted));
    }

    // ─── HEATMAP ─────────────────────────────────────────────────────────────

    [HttpGet("heatmap")]
    public async Task<IActionResult> Heatmap(
        [FromQuery] int? unitId,
        [FromQuery] DateTime? startDate,
        [FromQuery] DateTime? endDate)
    {
        if (ValidatePeriod(startDate, endDate) is { } v) return v;
        var (err, tenantId) = await ResolveAsync(unitId);
        if (err is not null) return err;
        return Ok(await insights.GetHeatmapAsync(tenantId, unitId,
            startDate, endDate, HttpContext.RequestAborted));
    }

    // ─── COHORT ──────────────────────────────────────────────────────────────

    [HttpGet("cohort")]
    public async Task<IActionResult> Cohort(
        [FromQuery] int? unitId,
        [FromQuery] DateTime? startDate,
        [FromQuery] DateTime? endDate,
        [FromQuery] string granularity = "week")
    {
        if (ValidatePeriod(startDate, endDate) is { } v) return v;
        var (err, tenantId) = await ResolveAsync(unitId);
        if (err is not null) return err;
        return Ok(await insights.GetCohortAsync(tenantId, unitId,
            startDate, endDate, granularity, HttpContext.RequestAborted));
    }

    // ─── LOST REASONS ────────────────────────────────────────────────────────

    [HttpGet("lost-reasons")]
    public async Task<IActionResult> LostReasons(
        [FromQuery] int? unitId,
        [FromQuery] DateTime? startDate,
        [FromQuery] DateTime? endDate)
    {
        if (ValidatePeriod(startDate, endDate) is { } v) return v;
        var (err, tenantId) = await ResolveAsync(unitId);
        if (err is not null) return err;
        return Ok(await insights.GetLostReasonsAsync(tenantId, unitId,
            startDate, endDate, HttpContext.RequestAborted));
    }

    // ─── FORECAST ────────────────────────────────────────────────────────────

    [HttpGet("forecast")]
    public async Task<IActionResult> Forecast(
        [FromQuery] int? unitId,
        [FromQuery] int horizonDays = 30)
    {
        var (err, tenantId) = await ResolveAsync(unitId);
        if (err is not null) return err;
        return Ok(await insights.GetForecastAsync(tenantId, unitId,
            horizonDays, HttpContext.RequestAborted));
    }

    // ─── GEO ─────────────────────────────────────────────────────────────────

    [HttpGet("geo")]
    public async Task<IActionResult> Geo(
        [FromQuery] int? unitId,
        [FromQuery] DateTime? startDate,
        [FromQuery] DateTime? endDate)
    {
        if (ValidatePeriod(startDate, endDate) is { } v) return v;
        var (err, tenantId) = await ResolveAsync(unitId);
        if (err is not null) return err;
        return Ok(await insights.GetGeoAsync(tenantId, unitId,
            startDate, endDate, HttpContext.RequestAborted));
    }

    // ─── QUALITY SCORE ───────────────────────────────────────────────────────

    [HttpGet("quality-score")]
    public async Task<IActionResult> QualityScore(
        [FromQuery] int? unitId,
        [FromQuery] DateTime? startDate,
        [FromQuery] DateTime? endDate)
    {
        if (ValidatePeriod(startDate, endDate) is { } v) return v;
        var (err, tenantId) = await ResolveAsync(unitId);
        if (err is not null) return err;
        return Ok(await insights.GetQualityScoreAsync(tenantId, unitId,
            startDate, endDate, HttpContext.RequestAborted));
    }
}
