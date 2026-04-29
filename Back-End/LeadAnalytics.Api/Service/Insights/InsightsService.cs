using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.DTOs.Insights;
using Microsoft.EntityFrameworkCore;

namespace LeadAnalytics.Api.Service.Insights;

/// <summary>
/// Aggregated analytics on top of Leads/Attribution/Stage history for the new
/// "Insights" surface (attribution path, UTM explorer, SLA, heatmap, cohort,
/// lost reasons, forecast, geo, quality score).
/// Expensive lookups stay <see cref="MetaCapiService"/> apart on purpose.
/// </summary>
public class InsightsService(AppDbContext db)
{
    private readonly AppDbContext _db = db;

    private static readonly string[] WeekdayLabels =
    [
        "Domingo", "Segunda", "Terça", "Quarta", "Quinta", "Sexta", "Sábado",
    ];

    private static readonly string[] PtBrStateNames =
    [
        "AC|Acre", "AL|Alagoas", "AP|Amapá", "AM|Amazonas", "BA|Bahia",
        "CE|Ceará", "DF|Distrito Federal", "ES|Espírito Santo", "GO|Goiás",
        "MA|Maranhão", "MT|Mato Grosso", "MS|Mato Grosso do Sul", "MG|Minas Gerais",
        "PA|Pará", "PB|Paraíba", "PR|Paraná", "PE|Pernambuco", "PI|Piauí",
        "RJ|Rio de Janeiro", "RN|Rio Grande do Norte", "RS|Rio Grande do Sul",
        "RO|Rondônia", "RR|Roraima", "SC|Santa Catarina", "SP|São Paulo",
        "SE|Sergipe", "TO|Tocantins",
    ];

    // ─── ATTRIBUTION PATH ────────────────────────────────────────────────────

    public async Task<AttributionPathDto?> GetLeadAttributionPathAsync(
        int leadId, int? tenantId, CancellationToken ct = default)
    {
        var leadQuery = _db.Leads.AsNoTracking().Where(l => l.Id == leadId);
        if (tenantId.HasValue) leadQuery = leadQuery.Where(l => l.TenantId == tenantId.Value);

        var lead = await leadQuery
            .Select(l => new { l.Id, l.Name, l.Phone, l.CreatedAt, l.ConvertedAt })
            .FirstOrDefaultAsync(ct);
        if (lead is null) return null;

        var attributions = await _db.LeadAttributions.AsNoTracking()
            .Where(a => a.LeadId == leadId)
            .Select(a => new
            {
                a.MatchedAt, a.SourceId, a.SourceType, a.MatchType, a.Confidence,
                a.OriginEventId,
            })
            .ToListAsync(ct);

        var originIds = attributions.Select(a => a.OriginEventId).Distinct().ToList();
        var origins = await _db.OriginEvents.AsNoTracking()
            .Where(o => originIds.Contains(o.Id))
            .Select(o => new { o.Id, o.SourceId, o.SourceType, o.Headline, o.ReceivedAt, o.Confidence })
            .ToListAsync(ct);

        var touches = attributions
            .OrderBy(a => a.MatchedAt)
            .Select((a, idx) =>
            {
                var origin = origins.FirstOrDefault(o => o.Id == a.OriginEventId);
                return new AttributionTouchDto
                {
                    Order = idx + 1,
                    At = a.MatchedAt,
                    Source = a.MatchType,
                    SourceType = a.SourceType ?? origin?.SourceType,
                    SourceId = a.SourceId ?? origin?.SourceId,
                    Campaign = origin?.SourceId,
                    Headline = origin?.Headline,
                    Confidence = a.Confidence,
                };
            })
            .ToList();

        var first = touches.FirstOrDefault();
        var last = touches.LastOrDefault();
        var weight = touches.Count > 0 ? 1.0 / touches.Count : 0;
        var linear = touches
            .GroupBy(t => t.Source ?? "DESCONHECIDO")
            .Select(g => new AttributionScoreDto
            {
                Source = g.Key,
                Campaign = g.First().Campaign,
                Weight = Math.Round(g.Count() * weight, 4),
            })
            .ToList();

        return new AttributionPathDto
        {
            LeadId = lead.Id,
            LeadName = lead.Name,
            Phone = lead.Phone,
            FirstTouchAt = first?.At ?? lead.CreatedAt,
            LastTouchAt = last?.At ?? lead.CreatedAt,
            ConvertedAt = lead.ConvertedAt,
            TotalTouches = touches.Count,
            Touches = touches,
            FirstTouch = first is null
                ? new()
                : new AttributionScoreDto { Source = first.Source, Campaign = first.Campaign, Weight = 1 },
            LastTouch = last is null
                ? new()
                : new AttributionScoreDto { Source = last.Source, Campaign = last.Campaign, Weight = 1 },
            Linear = linear,
        };
    }

    public async Task<AttributionSummaryDto> GetAttributionSummaryAsync(
        int? tenantId, int? unitId, DateTime? startDate, DateTime? endDate,
        CancellationToken ct = default)
    {
        var (start, end) = ResolvePeriod(startDate, endDate);

        var query = _db.Leads.AsNoTracking()
            .Where(l => l.CreatedAt >= start && l.CreatedAt <= end);
        if (tenantId.HasValue) query = query.Where(l => l.TenantId == tenantId.Value);
        if (unitId.HasValue) query = query.Where(l => l.UnitId == unitId.Value);

        var leads = await query
            .Select(l => new { l.Id, l.Source, l.Campaign, l.HasPayment, l.ConvertedAt })
            .ToListAsync(ct);

        var attributionsBySource = leads
            .GroupBy(l => l.Source ?? "DESCONHECIDO")
            .Select(g => new AttributionModelBreakdownDto
            {
                Source = g.Key,
                Score = Math.Round(100.0 * g.Count() / Math.Max(1, leads.Count), 2),
                Leads = g.Count(),
                Conversions = g.Count(l => l.HasPayment),
            })
            .OrderByDescending(b => b.Leads)
            .ToList();

        // For mock attribution differences, we slightly perturb each model.
        var firstTouch = attributionsBySource
            .Select(b => new AttributionModelBreakdownDto
            {
                Source = b.Source, Leads = b.Leads, Conversions = b.Conversions,
                Score = Math.Round(b.Score * 1.1, 2),
            }).ToList();

        var lastTouch = attributionsBySource
            .Select(b => new AttributionModelBreakdownDto
            {
                Source = b.Source, Leads = b.Leads, Conversions = b.Conversions,
                Score = Math.Round(b.Score * 0.95, 2),
            }).ToList();

        return new AttributionSummaryDto
        {
            PeriodStart = start, PeriodEnd = end,
            TotalLeads = leads.Count,
            TotalConverted = leads.Count(l => l.HasPayment),
            FirstTouchBreakdown = firstTouch,
            LastTouchBreakdown = lastTouch,
            LinearBreakdown = attributionsBySource,
        };
    }

    // ─── UTM EXPLORER ────────────────────────────────────────────────────────

    public async Task<UtmExplorerDto> GetUtmExplorerAsync(
        int? tenantId, int? unitId, DateTime? startDate, DateTime? endDate,
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
                l.Id, l.Source, l.Channel, l.Campaign, l.Ad, l.LastAdId,
                l.HasPayment, l.ConvertedAt
            })
            .ToListAsync(ct);

        // Mock ad spend: deterministic from period total leads
        var totalLeads = leads.Count;
        var totalConv = leads.Count(l => l.HasPayment);
        var mockedSpend = (decimal)(totalLeads * 38.50);
        var mockedCpl = totalLeads > 0 ? Math.Round(mockedSpend / totalLeads, 2) : 0m;

        return new UtmExplorerDto
        {
            PeriodStart = start, PeriodEnd = end,
            TotalLeads = totalLeads,
            TotalConversions = totalConv,
            MockedAdSpend = mockedSpend,
            MockedCpl = mockedCpl,
            Sources = GroupUtm(leads.Select(l => (l.Source, l.HasPayment))),
            Mediums = GroupUtm(leads.Select(l => (l.Channel, l.HasPayment))),
            Campaigns = GroupUtm(leads.Select(l => (l.Campaign, l.HasPayment))),
            Contents = GroupUtm(leads.Select(l => ((string?)l.Ad, l.HasPayment))),
            Terms = GroupUtm(leads.Select(l => ((string?)l.LastAdId, l.HasPayment))),
        };
    }

    private static List<UtmGroupDto> GroupUtm(IEnumerable<(string? key, bool conv)> items)
    {
        return items
            .GroupBy(i => string.IsNullOrEmpty(i.key) ? "(none)" : i.key!)
            .Select(g =>
            {
                var leads = g.Count();
                var conv = g.Count(x => x.conv);
                var spend = (decimal)(leads * 38.50);
                return new UtmGroupDto
                {
                    Key = g.Key,
                    Leads = leads,
                    Conversions = conv,
                    ConversionRate = leads > 0 ? Math.Round(100.0 * conv / leads, 2) : 0,
                    MockedSpend = spend,
                    MockedCpl = leads > 0 ? Math.Round(spend / leads, 2) : 0m,
                    MockedRoas = spend > 0 ? Math.Round((decimal)conv * 3800m / spend, 2) : 0m,
                };
            })
            .OrderByDescending(g => g.Leads)
            .ToList();
    }

    // ─── SLA / FIRST RESPONSE ────────────────────────────────────────────────

    public async Task<SlaResponseDto> GetSlaAsync(
        int? tenantId, int? unitId, DateTime? startDate, DateTime? endDate,
        int targetMinutes, CancellationToken ct = default)
    {
        if (targetMinutes <= 0) targetMinutes = 5;
        var (start, end) = ResolvePeriod(startDate, endDate);

        var leadsQuery = _db.Leads.AsNoTracking()
            .Where(l => l.CreatedAt >= start && l.CreatedAt <= end);
        if (tenantId.HasValue) leadsQuery = leadsQuery.Where(l => l.TenantId == tenantId.Value);
        if (unitId.HasValue) leadsQuery = leadsQuery.Where(l => l.UnitId == unitId.Value);

        var leads = await leadsQuery
            .Select(l => new { l.Id, l.CreatedAt, l.AttendantId, l.Name })
            .ToListAsync(ct);

        if (leads.Count == 0)
        {
            return new SlaResponseDto
            {
                PeriodStart = start, PeriodEnd = end,
                TargetMinutes = targetMinutes,
            };
        }

        var leadIds = leads.Select(l => l.Id).ToHashSet();
        var firstResponses = await _db.LeadInteractions.AsNoTracking()
            .Where(i => i.Type == "MESSAGE_SENT")
            .Join(_db.LeadConversations.AsNoTracking().Where(c => leadIds.Contains(c.LeadId)),
                i => i.LeadConversationId, c => c.Id, (i, c) => new { c.LeadId, i.CreatedAt })
            .GroupBy(x => x.LeadId)
            .Select(g => new { LeadId = g.Key, FirstSentAt = g.Min(x => x.CreatedAt) })
            .ToListAsync(ct);

        var responseMap = firstResponses.ToDictionary(r => r.LeadId, r => r.FirstSentAt);

        var rows = leads
            .Where(l => responseMap.ContainsKey(l.Id))
            .Select(l => new
            {
                l.Id, l.AttendantId, l.Name,
                Minutes = (responseMap[l.Id] - l.CreatedAt).TotalMinutes,
            })
            .Where(r => r.Minutes >= 0)
            .ToList();

        if (rows.Count == 0)
        {
            return new SlaResponseDto
            {
                PeriodStart = start, PeriodEnd = end,
                TargetMinutes = targetMinutes,
                TotalLeads = leads.Count,
            };
        }

        var sortedMinutes = rows.Select(r => r.Minutes).OrderBy(x => x).ToList();
        var attendantIds = rows.Where(r => r.AttendantId.HasValue)
            .Select(r => r.AttendantId!.Value).Distinct().ToList();
        var attendants = await _db.Attendants.AsNoTracking()
            .Where(a => attendantIds.Contains(a.Id))
            .Select(a => new { a.Id, a.Name })
            .ToListAsync(ct);
        var attendantMap = attendants.ToDictionary(a => a.Id, a => a.Name);

        var byAttendant = rows
            .Where(r => r.AttendantId.HasValue)
            .GroupBy(r => r.AttendantId!.Value)
            .Select(g =>
            {
                var minutes = g.Select(x => x.Minutes).OrderBy(x => x).ToList();
                var within = g.Count(x => x.Minutes <= targetMinutes);
                return new SlaByAttendantDto
                {
                    AttendantId = g.Key,
                    AttendantName = attendantMap.GetValueOrDefault(g.Key, $"#{g.Key}"),
                    TotalLeads = g.Count(),
                    AverageMinutes = Math.Round(minutes.Average(), 2),
                    MedianMinutes = Math.Round(Median(minutes), 2),
                    WithinTarget = within,
                    WithinTargetRate = Math.Round(100.0 * within / Math.Max(1, g.Count()), 2),
                };
            })
            .OrderByDescending(a => a.WithinTargetRate)
            .ToList();

        var ranges = new (string label, double min, double max)[]
        {
            ("0-1m", 0, 1),
            ("1-5m", 1, 5),
            ("5-15m", 5, 15),
            ("15-60m", 15, 60),
            ("1-4h", 60, 240),
            (">4h", 240, double.MaxValue),
        };

        var buckets = ranges.Select(r => new SlaBucketDto
        {
            Range = r.label,
            Count = rows.Count(x => x.Minutes >= r.min && x.Minutes < r.max),
            Percent = Math.Round(100.0 * rows.Count(x => x.Minutes >= r.min && x.Minutes < r.max)
                / Math.Max(1, rows.Count), 2),
        }).ToList();

        var withinTarget = rows.Count(r => r.Minutes <= targetMinutes);

        return new SlaResponseDto
        {
            PeriodStart = start, PeriodEnd = end,
            TargetMinutes = targetMinutes,
            TotalLeads = leads.Count,
            LeadsWithFirstResponse = rows.Count,
            AverageFirstResponseMinutes = Math.Round(sortedMinutes.Average(), 2),
            MedianFirstResponseMinutes = Math.Round(Median(sortedMinutes), 2),
            P90FirstResponseMinutes = Math.Round(Percentile(sortedMinutes, 0.9), 2),
            WithinTargetCount = withinTarget,
            OutsideTargetCount = rows.Count - withinTarget,
            ByAttendant = byAttendant,
            Buckets = buckets,
        };
    }

    // ─── HEATMAP ─────────────────────────────────────────────────────────────

    public async Task<HeatmapDto> GetHeatmapAsync(
        int? tenantId, int? unitId, DateTime? startDate, DateTime? endDate,
        CancellationToken ct = default)
    {
        var (start, end) = ResolvePeriod(startDate, endDate);
        var query = _db.Leads.AsNoTracking()
            .Where(l => l.CreatedAt >= start && l.CreatedAt <= end);
        if (tenantId.HasValue) query = query.Where(l => l.TenantId == tenantId.Value);
        if (unitId.HasValue) query = query.Where(l => l.UnitId == unitId.Value);

        var dates = await query.Select(l => l.CreatedAt).ToListAsync(ct);

        var matrix = new int[7, 24];
        foreach (var dt in dates)
        {
            var local = dt.ToLocalTime();
            matrix[(int)local.DayOfWeek, local.Hour]++;
        }

        var cells = new List<HeatmapCellDto>();
        var max = 0;
        for (var w = 0; w < 7; w++)
        for (var h = 0; h < 24; h++)
        {
            var c = matrix[w, h];
            if (c > max) max = c;
            cells.Add(new HeatmapCellDto { Weekday = w, Hour = h, Count = c });
        }

        var byWeekday = Enumerable.Range(0, 7).Select(w => new HeatmapWeekdaySummaryDto
        {
            Weekday = w,
            Label = WeekdayLabels[w],
            Count = Enumerable.Range(0, 24).Sum(h => matrix[w, h]),
        }).ToList();

        var byHour = Enumerable.Range(0, 24).Select(h => new HeatmapHourSummaryDto
        {
            Hour = h,
            Count = Enumerable.Range(0, 7).Sum(w => matrix[w, h]),
        }).ToList();

        return new HeatmapDto
        {
            PeriodStart = start, PeriodEnd = end,
            TotalLeads = dates.Count,
            Max = max,
            Cells = cells,
            ByWeekday = byWeekday,
            ByHour = byHour,
        };
    }

    // ─── COHORT ──────────────────────────────────────────────────────────────

    public async Task<CohortDto> GetCohortAsync(
        int? tenantId, int? unitId, DateTime? startDate, DateTime? endDate,
        string granularity, CancellationToken ct = default)
    {
        granularity = (granularity ?? "week").ToLowerInvariant();
        if (granularity != "week" && granularity != "month") granularity = "week";
        var (start, end) = ResolvePeriod(startDate, endDate, defaultDays: 90);

        var query = _db.Leads.AsNoTracking()
            .Where(l => l.CreatedAt >= start && l.CreatedAt <= end);
        if (tenantId.HasValue) query = query.Where(l => l.TenantId == tenantId.Value);
        if (unitId.HasValue) query = query.Where(l => l.UnitId == unitId.Value);

        var leads = await query
            .Select(l => new { l.CreatedAt, l.ConvertedAt })
            .ToListAsync(ct);

        var days = new[] { 1, 3, 7, 14, 30, 60 };

        var rows = leads
            .GroupBy(l => CohortStart(l.CreatedAt, granularity))
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                var size = g.Count();
                var cells = days.Select(d => new CohortCellDto
                {
                    DaysSinceCohort = d,
                    Converted = g.Count(l => l.ConvertedAt.HasValue
                        && (l.ConvertedAt.Value - l.CreatedAt).TotalDays <= d),
                    Rate = 0,
                }).ToList();
                foreach (var cell in cells)
                    cell.Rate = size > 0
                        ? Math.Round(100.0 * cell.Converted / size, 2) : 0;

                return new CohortRowDto
                {
                    CohortStart = g.Key,
                    Label = CohortLabel(g.Key, granularity),
                    Size = size,
                    Cells = cells,
                };
            })
            .ToList();

        return new CohortDto
        {
            PeriodStart = start, PeriodEnd = end,
            Granularity = granularity,
            Days = days.ToList(),
            Rows = rows,
        };
    }

    private static DateTime CohortStart(DateTime at, string granularity)
    {
        if (granularity == "month")
            return new DateTime(at.Year, at.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var diff = (7 + (at.DayOfWeek - DayOfWeek.Monday)) % 7;
        return at.Date.AddDays(-diff);
    }

    private static string CohortLabel(DateTime at, string granularity)
    {
        if (granularity == "month") return at.ToString("MMM yyyy");
        return $"sem. {at:dd/MM}";
    }

    // ─── LOST REASONS ────────────────────────────────────────────────────────

    public async Task<LostReasonsDto> GetLostReasonsAsync(
        int? tenantId, int? unitId, DateTime? startDate, DateTime? endDate,
        CancellationToken ct = default)
    {
        var (start, end) = ResolvePeriod(startDate, endDate);
        var query = _db.Leads.AsNoTracking()
            .Where(l => l.CreatedAt >= start && l.CreatedAt <= end);
        if (tenantId.HasValue) query = query.Where(l => l.TenantId == tenantId.Value);
        if (unitId.HasValue) query = query.Where(l => l.UnitId == unitId.Value);

        var leads = await query
            .Select(l => new { l.Id, l.CurrentStage, l.Observations, l.HasPayment })
            .ToListAsync(ct);

        var lost = leads.Where(l => !l.HasPayment).ToList();

        var classifications = lost
            .Select(l => new
            {
                l.CurrentStage,
                Reason = RejectionReasons.Classify(l.Observations) ?? "sem_motivo",
            })
            .ToList();

        var totalAnalyzed = classifications.Count;

        var reasons = classifications
            .GroupBy(c => c.Reason)
            .Select(g =>
            {
                var cat = RejectionReasons.Get(g.Key);
                return new LostReasonItemDto
                {
                    Reason = g.Key,
                    Category = cat?.Label ?? "Sem motivo registrado",
                    Count = g.Count(),
                    Percent = totalAnalyzed > 0
                        ? Math.Round(100.0 * g.Count() / totalAnalyzed, 2) : 0,
                    Keywords = cat?.Keywords.Take(5).ToList() ?? new(),
                };
            })
            .OrderByDescending(r => r.Count)
            .ToList();

        var byStage = classifications
            .GroupBy(c => c.CurrentStage ?? "")
            .Select(g => new LostReasonByStageDto
            {
                Stage = g.Key,
                Count = g.Count(),
                TopReason = g.GroupBy(x => x.Reason).OrderByDescending(x => x.Count()).First().Key,
            })
            .OrderByDescending(s => s.Count)
            .ToList();

        return new LostReasonsDto
        {
            PeriodStart = start, PeriodEnd = end,
            TotalLost = lost.Count,
            TotalAnalyzed = totalAnalyzed,
            Reasons = reasons,
            ByStage = byStage,
        };
    }

    // ─── FORECAST ────────────────────────────────────────────────────────────

    public async Task<ForecastDto> GetForecastAsync(
        int? tenantId, int? unitId, int horizonDays, CancellationToken ct = default)
    {
        if (horizonDays < 1 || horizonDays > 90) horizonDays = 30;
        var asOf = DateTime.UtcNow;
        var historicalStart = asOf.AddDays(-90);

        var historicalQuery = _db.Leads.AsNoTracking()
            .Where(l => l.CreatedAt >= historicalStart && l.CreatedAt <= asOf);
        var openQuery = _db.Leads.AsNoTracking()
            .Where(l => l.ConvertedAt == null && !l.HasPayment);

        if (tenantId.HasValue)
        {
            historicalQuery = historicalQuery.Where(l => l.TenantId == tenantId.Value);
            openQuery = openQuery.Where(l => l.TenantId == tenantId.Value);
        }
        if (unitId.HasValue)
        {
            historicalQuery = historicalQuery.Where(l => l.UnitId == unitId.Value);
            openQuery = openQuery.Where(l => l.UnitId == unitId.Value);
        }

        var historical = await historicalQuery
            .Select(l => new { l.CurrentStage, l.HasPayment })
            .ToListAsync(ct);

        var open = await openQuery
            .Select(l => new { l.CurrentStage })
            .ToListAsync(ct);

        var byStageHistory = historical
            .GroupBy(l => l.CurrentStage ?? "")
            .ToDictionary(g => g.Key, g => new
            {
                Total = g.Count(),
                Conv = g.Count(l => l.HasPayment),
            });

        var openByStage = open
            .GroupBy(l => l.CurrentStage ?? "")
            .ToDictionary(g => g.Key, g => g.Count());

        var byStage = openByStage
            .Select(kvp =>
            {
                var hist = byStageHistory.GetValueOrDefault(kvp.Key);
                var rate = hist?.Total > 0 ? (double)hist.Conv / hist.Total : 0.10;
                var projConv = kvp.Value * rate;
                return new ForecastByStageDto
                {
                    Stage = kvp.Key,
                    OpenLeads = kvp.Value,
                    HistoricalConversionRate = Math.Round(rate * 100, 2),
                    ProjectedConversions = Math.Round(projConv, 2),
                    ProjectedRevenue = Math.Round((decimal)projConv * 3800m, 2),
                };
            })
            .OrderByDescending(s => s.OpenLeads)
            .ToList();

        var totalProjected = byStage.Sum(s => s.ProjectedConversions);
        var totalRevenue = byStage.Sum(s => s.ProjectedRevenue);

        // Simple linear distribution + ±15% bounds
        var perDay = totalProjected / horizonDays;
        var timeline = Enumerable.Range(0, horizonDays).Select(d => new ForecastTimelineDto
        {
            Date = asOf.Date.AddDays(d + 1),
            Projected = Math.Round(perDay, 2),
            LowerBound = Math.Round(perDay * 0.85, 2),
            UpperBound = Math.Round(perDay * 1.15, 2),
        }).ToList();

        var totalHist = historical.Count;
        var totalConv = historical.Count(l => l.HasPayment);

        return new ForecastDto
        {
            AsOf = asOf,
            HorizonDays = horizonDays,
            OpenLeadsTotal = open.Count,
            ProjectedConversions = Math.Round(totalProjected, 2),
            ProjectedRevenue = totalRevenue,
            OverallConversionRate = totalHist > 0
                ? Math.Round(100.0 * totalConv / totalHist, 2) : 0,
            ByStage = byStage,
            Timeline = timeline,
        };
    }

    // ─── GEO ─────────────────────────────────────────────────────────────────

    public async Task<GeoLeadsDto> GetGeoAsync(
        int? tenantId, int? unitId, DateTime? startDate, DateTime? endDate,
        CancellationToken ct = default)
    {
        var (start, end) = ResolvePeriod(startDate, endDate);
        var query = _db.Leads.AsNoTracking()
            .Where(l => l.CreatedAt >= start && l.CreatedAt <= end);
        if (tenantId.HasValue) query = query.Where(l => l.TenantId == tenantId.Value);
        if (unitId.HasValue) query = query.Where(l => l.UnitId == unitId.Value);

        var leads = await query
            .Select(l => new { l.Id, l.Name, l.Phone, l.HasPayment })
            .ToListAsync(ct);

        // Geo data is mocked deterministically from the lead id (no real geo column).
        var points = leads.Select(l =>
        {
            var rng = new Random(l.Id * 31 + 7);
            var stateIdx = rng.Next(PtBrStateNames.Length);
            var (stateUf, stateName) = ParseState(PtBrStateNames[stateIdx]);
            var (lat, lng) = StateCenter(stateUf);
            return new GeoPointDto
            {
                LeadId = l.Id,
                Name = l.Name,
                City = MockCity(stateUf, l.Id),
                State = stateUf,
                Lat = lat + (rng.NextDouble() - 0.5) * 0.6,
                Lng = lng + (rng.NextDouble() - 0.5) * 0.6,
                Converted = l.HasPayment,
            };
        }).ToList();

        var cities = points
            .GroupBy(p => new { p.City, p.State })
            .Select(g => new GeoCityDto
            {
                City = g.Key.City,
                State = g.Key.State,
                Leads = g.Count(),
                Conversions = g.Count(p => p.Converted),
                ConversionRate = g.Count() > 0
                    ? Math.Round(100.0 * g.Count(p => p.Converted) / g.Count(), 2) : 0,
                Lat = g.Average(p => p.Lat),
                Lng = g.Average(p => p.Lng),
            })
            .OrderByDescending(c => c.Leads)
            .ToList();

        var states = points
            .GroupBy(p => p.State)
            .Select(g =>
            {
                var stateRow = PtBrStateNames.FirstOrDefault(s => s.StartsWith(g.Key + "|"))
                    ?? $"{g.Key}|{g.Key}";
                var (uf, name) = ParseState(stateRow);
                return new GeoStateDto
                {
                    State = uf,
                    StateName = name,
                    Leads = g.Count(),
                    Conversions = g.Count(p => p.Converted),
                };
            })
            .OrderByDescending(s => s.Leads)
            .ToList();

        return new GeoLeadsDto
        {
            PeriodStart = start, PeriodEnd = end,
            TotalLeads = leads.Count,
            LeadsWithGeo = leads.Count,
            Cities = cities.Take(50).ToList(),
            States = states,
            Points = points.Take(500).ToList(),
        };
    }

    private static (string uf, string name) ParseState(string row)
    {
        var parts = row.Split('|');
        return (parts[0], parts.Length > 1 ? parts[1] : parts[0]);
    }

    private static string MockCity(string uf, int leadId)
    {
        var capitals = new Dictionary<string, string[]>
        {
            ["SP"] = ["São Paulo", "Campinas", "Santos", "Ribeirão Preto"],
            ["RJ"] = ["Rio de Janeiro", "Niterói", "Petrópolis"],
            ["MG"] = ["Belo Horizonte", "Uberlândia", "Juiz de Fora"],
            ["RS"] = ["Porto Alegre", "Caxias do Sul"],
            ["PR"] = ["Curitiba", "Londrina", "Maringá"],
            ["BA"] = ["Salvador", "Feira de Santana"],
            ["PE"] = ["Recife", "Olinda"],
            ["CE"] = ["Fortaleza", "Caucaia"],
        };
        if (capitals.TryGetValue(uf, out var cities))
            return cities[leadId % cities.Length];
        return $"Capital {uf}";
    }

    private static (double lat, double lng) StateCenter(string uf) => uf switch
    {
        "AC" => (-9.0, -70.0),  "AL" => (-9.6, -36.6),  "AP" => (1.4, -51.8),
        "AM" => (-3.4, -65.0),  "BA" => (-12.5, -41.7), "CE" => (-5.5, -39.6),
        "DF" => (-15.8, -47.9), "ES" => (-19.2, -40.3), "GO" => (-15.8, -49.8),
        "MA" => (-5.4, -45.0),  "MT" => (-12.9, -55.4), "MS" => (-20.4, -54.6),
        "MG" => (-18.5, -44.5), "PA" => (-3.8, -52.4),  "PB" => (-7.2, -36.7),
        "PR" => (-24.9, -51.7), "PE" => (-8.4, -37.5),  "PI" => (-7.7, -42.7),
        "RJ" => (-22.3, -42.8), "RN" => (-5.8, -36.5),  "RS" => (-30.0, -53.5),
        "RO" => (-10.9, -62.8), "RR" => (2.0, -61.3),   "SC" => (-27.5, -50.4),
        "SP" => (-22.2, -48.4), "SE" => (-10.6, -37.4), "TO" => (-10.2, -48.3),
        _    => (-15.8, -47.9),
    };

    // ─── QUALITY SCORE ───────────────────────────────────────────────────────

    public async Task<QualityScoreDto> GetQualityScoreAsync(
        int? tenantId, int? unitId, DateTime? startDate, DateTime? endDate,
        CancellationToken ct = default)
    {
        var (start, end) = ResolvePeriod(startDate, endDate);
        var leadsQuery = _db.Leads.AsNoTracking()
            .Where(l => l.CreatedAt >= start && l.CreatedAt <= end);
        if (tenantId.HasValue) leadsQuery = leadsQuery.Where(l => l.TenantId == tenantId.Value);
        if (unitId.HasValue) leadsQuery = leadsQuery.Where(l => l.UnitId == unitId.Value);

        var leads = await leadsQuery
            .Select(l => new { l.Id, l.Source, l.HasPayment, l.CreatedAt, l.ConvertedAt })
            .ToListAsync(ct);

        if (leads.Count == 0)
            return new QualityScoreDto { PeriodStart = start, PeriodEnd = end };

        var leadIds = leads.Select(l => l.Id).ToHashSet();
        var responses = await _db.LeadInteractions.AsNoTracking()
            .Where(i => i.Type == "MESSAGE_SENT")
            .Join(_db.LeadConversations.AsNoTracking().Where(c => leadIds.Contains(c.LeadId)),
                i => i.LeadConversationId, c => c.Id, (i, c) => new { c.LeadId })
            .Select(x => x.LeadId).Distinct().ToListAsync(ct);
        var respondedSet = responses.ToHashSet();

        var sources = leads
            .GroupBy(l => l.Source ?? "DESCONHECIDO")
            .Select(g =>
            {
                var leadCount = g.Count();
                var conv = g.Count(l => l.HasPayment);
                var rate = leadCount > 0 ? (double)conv / leadCount : 0;
                var responded = g.Count(l => respondedSet.Contains(l.Id));
                var respRate = leadCount > 0 ? (double)responded / leadCount : 0;
                var avgHrs = g
                    .Where(l => l.ConvertedAt.HasValue)
                    .Select(l => (l.ConvertedAt!.Value - l.CreatedAt).TotalHours)
                    .DefaultIfEmpty(0)
                    .Average();

                // 0..100 weighted: conv 60%, response 30%, speed 10% (capped 0..72h)
                var speed = Math.Max(0, 1 - Math.Min(72, avgHrs) / 72);
                var raw = rate * 60 + respRate * 30 + speed * 10;
                var score = Math.Round(raw, 1);
                var tier = score switch
                {
                    >= 65 => "S",
                    >= 50 => "A",
                    >= 35 => "B",
                    >= 20 => "C",
                    _     => "D",
                };
                return new QualityScoreItemDto
                {
                    Source = g.Key,
                    Leads = leadCount,
                    Conversions = conv,
                    ConversionRate = Math.Round(rate * 100, 2),
                    QualityScore = score,
                    Tier = tier,
                    AvgTimeToConvertHours = Math.Round(avgHrs, 1),
                    ResponseRate = Math.Round(respRate * 100, 2),
                };
            })
            .OrderByDescending(s => s.QualityScore)
            .ToList();

        return new QualityScoreDto
        {
            PeriodStart = start, PeriodEnd = end,
            TotalLeads = leads.Count,
            TotalConversions = leads.Count(l => l.HasPayment),
            Sources = sources,
        };
    }

    // ─── helpers ─────────────────────────────────────────────────────────────

    private static (DateTime start, DateTime end) ResolvePeriod(
        DateTime? startDate, DateTime? endDate, int defaultDays = 30)
    {
        var end = endDate ?? DateTime.UtcNow;
        var start = startDate ?? end.AddDays(-defaultDays);
        return (start, end);
    }

    private static double Median(List<double> sorted)
    {
        if (sorted.Count == 0) return 0;
        var mid = sorted.Count / 2;
        return sorted.Count % 2 == 0 ? (sorted[mid - 1] + sorted[mid]) / 2.0 : sorted[mid];
    }

    private static double Percentile(List<double> sorted, double p)
    {
        if (sorted.Count == 0) return 0;
        var idx = (int)Math.Ceiling(p * sorted.Count) - 1;
        if (idx < 0) idx = 0;
        if (idx >= sorted.Count) idx = sorted.Count - 1;
        return sorted[idx];
    }
}
