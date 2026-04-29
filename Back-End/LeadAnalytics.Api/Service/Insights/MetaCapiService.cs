using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.DTOs.Insights;
using Microsoft.EntityFrameworkCore;

namespace LeadAnalytics.Api.Service.Insights;

/// <summary>
/// Mocked Meta Conversions API events service.
/// Generates deterministic synthetic CAPI events derived from real Leads,
/// so the front-end can be wired now and switched to a real sender later.
/// </summary>
public class MetaCapiService(AppDbContext db)
{
    private readonly AppDbContext _db = db;

    private static readonly string[] EventNames =
    [
        "Lead", "CompleteRegistration", "Schedule", "Contact", "Purchase",
    ];

    private static readonly string[] StatusValues =
    [
        "sent", "sent", "sent", "sent", "sent", "sent",
        "failed", "deduped", "pending",
    ];

    private static readonly string[] MatchQualities =
    [
        "GREAT_MATCH", "GREAT_MATCH", "GOOD_MATCH", "GOOD_MATCH", "POOR_MATCH",
    ];

    public async Task<CapiEventListDto> ListAsync(
        int? tenantId,
        int? unitId,
        DateTime? startDate,
        DateTime? endDate,
        string? eventName,
        string? status,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        if (page < 1) page = 1;
        if (pageSize is < 1 or > 200) pageSize = 50;

        var (start, end) = ResolvePeriod(startDate, endDate);

        var query = _db.Leads.AsNoTracking().AsQueryable();
        if (tenantId.HasValue) query = query.Where(l => l.TenantId == tenantId.Value);
        if (unitId.HasValue) query = query.Where(l => l.UnitId == unitId.Value);
        query = query.Where(l => l.CreatedAt >= start && l.CreatedAt <= end);

        var leads = await query
            .OrderByDescending(l => l.CreatedAt)
            .Take(2000)
            .Select(l => new
            {
                l.Id,
                l.Name,
                l.Phone,
                l.Email,
                l.Source,
                l.Campaign,
                l.LastAdId,
                l.IdFacebookApp,
                l.HasPayment,
                l.HasAppointment,
                l.CreatedAt,
                l.ConvertedAt,
                l.UnitId,
            })
            .ToListAsync(ct);

        var events = new List<CapiEventDto>();
        foreach (var lead in leads)
        {
            var rng = new Random(lead.Id * 31 + 7);

            // Lead event (always exists for every lead)
            events.Add(BuildEvent(lead.Id, lead.Name, lead.Phone, lead.Email,
                lead.Source, lead.Campaign, lead.LastAdId, lead.CreatedAt,
                "Lead", rng));

            if (lead.HasAppointment)
                events.Add(BuildEvent(lead.Id, lead.Name, lead.Phone, lead.Email,
                    lead.Source, lead.Campaign, lead.LastAdId,
                    lead.CreatedAt.AddHours(rng.Next(1, 48)),
                    "Schedule", rng));

            if (lead.HasPayment && lead.ConvertedAt.HasValue)
                events.Add(BuildEvent(lead.Id, lead.Name, lead.Phone, lead.Email,
                    lead.Source, lead.Campaign, lead.LastAdId,
                    lead.ConvertedAt.Value, "Purchase", rng,
                    value: 200 + rng.Next(0, 4000)));
        }

        if (!string.IsNullOrEmpty(eventName))
            events = events.Where(e => string.Equals(e.EventName, eventName, StringComparison.OrdinalIgnoreCase)).ToList();
        if (!string.IsNullOrEmpty(status))
            events = events.Where(e => string.Equals(e.Status, status, StringComparison.OrdinalIgnoreCase)).ToList();

        events = events.OrderByDescending(e => e.EventTime).ToList();

        var total = events.Count;
        var paged = events.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        return new CapiEventListDto
        {
            Total = total,
            Page = page,
            PageSize = pageSize,
            Items = paged,
            Stats = BuildStats(events),
        };
    }

    public async Task<CapiEventDto?> GetAsync(string id, int? tenantId, CancellationToken ct = default)
    {
        // ID format: "capi-{leadId}-{eventName}-{ticks}"
        var parts = id.Split('-');
        if (parts.Length < 4 || parts[0] != "capi") return null;
        if (!int.TryParse(parts[1], out var leadId)) return null;

        var lead = await _db.Leads.AsNoTracking()
            .Where(l => l.Id == leadId && (!tenantId.HasValue || l.TenantId == tenantId.Value))
            .Select(l => new
            {
                l.Id, l.Name, l.Phone, l.Email, l.Source, l.Campaign,
                l.LastAdId, l.CreatedAt, l.ConvertedAt
            })
            .FirstOrDefaultAsync(ct);

        if (lead is null) return null;

        var rng = new Random(lead.Id * 31 + 7);
        var ev = BuildEvent(lead.Id, lead.Name, lead.Phone, lead.Email,
            lead.Source, lead.Campaign, lead.LastAdId, lead.CreatedAt,
            parts[2], rng);
        ev.Id = id;
        return ev;
    }

    public async Task<CapiEventDto?> RetryAsync(string id, int? tenantId, CancellationToken ct = default)
    {
        var ev = await GetAsync(id, tenantId, ct);
        if (ev is null) return null;
        ev.Status = "sent";
        ev.SentAt = DateTime.UtcNow;
        ev.RetryCount += 1;
        ev.ErrorMessage = null;
        return ev;
    }

    public async Task<PixelHealthDto> GetPixelHealthAsync(
        int? tenantId,
        int? unitId,
        DateTime? startDate,
        DateTime? endDate,
        CancellationToken ct = default)
    {
        var (start, end) = ResolvePeriod(startDate, endDate);

        var query = _db.Leads.AsNoTracking()
            .Where(l => l.CreatedAt >= start && l.CreatedAt <= end);
        if (tenantId.HasValue) query = query.Where(l => l.TenantId == tenantId.Value);
        if (unitId.HasValue) query = query.Where(l => l.UnitId == unitId.Value);

        var leads = await query
            .Select(l => new
            {
                l.Id, l.Email, l.Phone, l.IdFacebookApp,
                l.UnitId, l.CreatedAt, l.LastAdId,
                UnitName = l.Unit != null ? l.Unit.Name : null,
            })
            .ToListAsync(ct);

        if (leads.Count == 0)
        {
            return new PixelHealthDto
            {
                PeriodStart = start,
                PeriodEnd = end,
                TotalEvents = 0,
                Alerts =
                [
                    new() { Severity = "info", Title = "Sem eventos no período",
                            Message = "Nenhum lead no período selecionado." }
                ],
            };
        }

        var emailHash = leads.Count(l => !string.IsNullOrEmpty(l.Email));
        var phoneHash = leads.Count(l => !string.IsNullOrEmpty(l.Phone));
        var fbApp = leads.Count(l => !string.IsNullOrEmpty(l.IdFacebookApp));

        // IP and fbp/fbc are not stored — we mock based on a deterministic hash so the number is stable.
        int ipCount = 0, fbpCount = 0, fbcCount = 0;
        double emqSum = 0;
        foreach (var l in leads)
        {
            var rng = new Random(l.Id * 31 + 7);
            if (rng.NextDouble() < 0.85) ipCount++;
            if (rng.NextDouble() < 0.72) fbpCount++;
            if (rng.NextDouble() < 0.55) fbcCount++;
            emqSum += 4 + rng.NextDouble() * 6; // 4..10
        }

        var byUnit = leads
            .Where(l => l.UnitId.HasValue)
            .GroupBy(l => new { Id = l.UnitId!.Value, Name = l.UnitName ?? $"Unidade {l.UnitId}" })
            .Select(g => new PixelHealthByUnitDto
            {
                UnitId = g.Key.Id,
                UnitName = g.Key.Name,
                TotalEvents = g.Count(),
                EmqScore = Math.Round(g.Average(x =>
                {
                    var rng = new Random(x.Id * 31 + 7);
                    return 4 + rng.NextDouble() * 6;
                }), 2),
                Coverage = Math.Round(
                    100.0 * g.Count(x =>
                        !string.IsNullOrEmpty(x.Email) || !string.IsNullOrEmpty(x.Phone))
                    / Math.Max(1, g.Count()), 1),
            })
            .OrderByDescending(u => u.TotalEvents)
            .ToList();

        var emailCov = 100.0 * emailHash / leads.Count;
        var phoneCov = 100.0 * phoneHash / leads.Count;
        var ipCov = 100.0 * ipCount / leads.Count;
        var fbpCov = 100.0 * fbpCount / leads.Count;
        var fbcCov = 100.0 * fbcCount / leads.Count;
        var avgEmq = emqSum / leads.Count;

        var alerts = new List<PixelHealthAlertDto>();
        if (emailCov < 60)
            alerts.Add(new() { Severity = "warning", Title = "Cobertura de email baixa",
                Message = $"Apenas {emailCov:F0}% dos eventos têm email. Recomendado >= 70%." });
        if (phoneCov < 80)
            alerts.Add(new() { Severity = "warning", Title = "Cobertura de telefone baixa",
                Message = $"Apenas {phoneCov:F0}% dos eventos têm telefone. Recomendado >= 90%." });
        if (avgEmq < 6)
            alerts.Add(new() { Severity = "critical", Title = "EMQ Score abaixo do recomendado",
                Message = $"EMQ médio = {avgEmq:F1}. Considere enriquecer eventos com IP/fbp/fbc." });
        if (alerts.Count == 0)
            alerts.Add(new() { Severity = "info", Title = "Saúde do pixel OK",
                Message = "Cobertura e EMQ dentro dos parâmetros recomendados." });

        return new PixelHealthDto
        {
            PeriodStart = start,
            PeriodEnd = end,
            TotalEvents = leads.Count,
            EmailHashCoverage = Math.Round(emailCov, 1),
            PhoneHashCoverage = Math.Round(phoneCov, 1),
            IpCoverage = Math.Round(ipCov, 1),
            FbpCoverage = Math.Round(fbpCov, 1),
            FbcCoverage = Math.Round(fbcCov, 1),
            AverageEmqScore = Math.Round(avgEmq, 2),
            DeduplicationRate = Math.Round(8.0 + (leads.Count % 7), 1),
            ByUnit = byUnit,
            Alerts = alerts,
        };
    }

    // ─── helpers ──────────────────────────────────────────────────────────────

    private static (DateTime start, DateTime end) ResolvePeriod(DateTime? startDate, DateTime? endDate)
    {
        var end = endDate ?? DateTime.UtcNow;
        var start = startDate ?? end.AddDays(-30);
        return (start, end);
    }

    private static CapiEventDto BuildEvent(
        int leadId,
        string? name,
        string? phone,
        string? email,
        string? source,
        string? campaign,
        string? adId,
        DateTime eventTime,
        string eventName,
        Random rng,
        decimal? value = null)
    {
        var status = StatusValues[rng.Next(StatusValues.Length)];
        var hasFbp = rng.NextDouble() < 0.72;
        var hasFbc = rng.NextDouble() < 0.55;
        var hasIp = rng.NextDouble() < 0.85;
        var emq = Math.Round(4 + rng.NextDouble() * 6, 2);
        var match = MatchQualities[rng.Next(MatchQualities.Length)];

        return new CapiEventDto
        {
            Id = $"capi-{leadId}-{eventName}-{eventTime.Ticks}",
            EventName = eventName,
            Status = status,
            EventTime = eventTime,
            SentAt = status is "sent" or "deduped" ? eventTime.AddSeconds(rng.Next(1, 30)) : null,
            Source = source,
            PixelId = "1234567890",
            LeadId = leadId,
            LeadName = name,
            Phone = phone,
            HasEmailHash = !string.IsNullOrEmpty(email),
            HasPhoneHash = !string.IsNullOrEmpty(phone),
            HasIp = hasIp,
            HasFbp = hasFbp,
            HasFbc = hasFbc,
            EmqScore = emq,
            MatchQuality = match,
            IsDeduped = status == "deduped",
            DedupedWith = status == "deduped" ? "browser-pixel" : null,
            RetryCount = status == "failed" ? rng.Next(1, 4) : 0,
            ErrorMessage = status == "failed" ? "InvalidParameterException: 'event_id' missing" : null,
            FbtraceId = status == "sent" ? $"AbCdE{rng.Next(10000, 99999)}" : null,
            Value = value,
            Currency = value.HasValue ? "BRL" : null,
            Campaign = campaign,
            AdId = adId,
        };
    }

    private static CapiEventStatsDto BuildStats(List<CapiEventDto> events)
    {
        if (events.Count == 0) return new CapiEventStatsDto();

        var sent = events.Count(e => e.Status == "sent");
        var failed = events.Count(e => e.Status == "failed");
        var deduped = events.Count(e => e.Status == "deduped");
        var pending = events.Count(e => e.Status == "pending");
        var received = events.Count;

        var byEventName = events
            .GroupBy(e => e.EventName)
            .ToDictionary(g => g.Key, g => g.Count());

        var byStatus = events
            .GroupBy(e => e.Status)
            .ToDictionary(g => g.Key, g => g.Count());

        var timeline = events
            .GroupBy(e => new DateTime(e.EventTime.Year, e.EventTime.Month, e.EventTime.Day))
            .OrderBy(g => g.Key)
            .Select(g => new CapiTimeBucketDto
            {
                Bucket = g.Key,
                Sent = g.Count(e => e.Status == "sent"),
                Failed = g.Count(e => e.Status == "failed"),
                Deduped = g.Count(e => e.Status == "deduped"),
            })
            .ToList();

        return new CapiEventStatsDto
        {
            Received = received,
            Sent = sent,
            Failed = failed,
            Deduped = deduped,
            Pending = pending,
            SuccessRate = Math.Round(100.0 * sent / Math.Max(1, received), 2),
            DedupRate = Math.Round(100.0 * deduped / Math.Max(1, received), 2),
            AverageEmqScore = Math.Round(events.Where(e => e.EmqScore.HasValue).Average(e => e.EmqScore!.Value), 2),
            ByEventName = byEventName,
            ByStatus = byStatus,
            Timeline = timeline,
        };
    }
}
