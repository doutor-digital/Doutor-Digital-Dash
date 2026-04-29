namespace LeadAnalytics.Api.DTOs.Insights;

public class AttributionPathDto
{
    public int LeadId { get; set; }
    public string LeadName { get; set; } = "";
    public string? Phone { get; set; }
    public DateTime? FirstTouchAt { get; set; }
    public DateTime? LastTouchAt { get; set; }
    public DateTime? ConvertedAt { get; set; }
    public int TotalTouches { get; set; }
    public List<AttributionTouchDto> Touches { get; set; } = new();
    public AttributionScoreDto FirstTouch { get; set; } = new();
    public AttributionScoreDto LastTouch { get; set; } = new();
    public List<AttributionScoreDto> Linear { get; set; } = new();
}

public class AttributionTouchDto
{
    public int Order { get; set; }
    public DateTime At { get; set; }
    public string? Source { get; set; }
    public string? SourceType { get; set; }
    public string? SourceId { get; set; }
    public string? Campaign { get; set; }
    public string? Headline { get; set; }
    public string Confidence { get; set; } = "LOW";
}

public class AttributionScoreDto
{
    public string? Source { get; set; }
    public string? Campaign { get; set; }
    public double Weight { get; set; }
}

public class AttributionSummaryDto
{
    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }
    public int TotalLeads { get; set; }
    public int TotalConverted { get; set; }
    public List<AttributionModelBreakdownDto> FirstTouchBreakdown { get; set; } = new();
    public List<AttributionModelBreakdownDto> LastTouchBreakdown { get; set; } = new();
    public List<AttributionModelBreakdownDto> LinearBreakdown { get; set; } = new();
}

public class AttributionModelBreakdownDto
{
    public string Source { get; set; } = "";
    public double Score { get; set; }
    public int Leads { get; set; }
    public int Conversions { get; set; }
}
