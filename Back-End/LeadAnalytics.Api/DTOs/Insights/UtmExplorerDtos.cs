namespace LeadAnalytics.Api.DTOs.Insights;

public class UtmExplorerDto
{
    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }
    public int TotalLeads { get; set; }
    public int TotalConversions { get; set; }
    public decimal MockedAdSpend { get; set; }
    public decimal MockedCpl { get; set; }
    public List<UtmGroupDto> Sources { get; set; } = new();
    public List<UtmGroupDto> Mediums { get; set; } = new();
    public List<UtmGroupDto> Campaigns { get; set; } = new();
    public List<UtmGroupDto> Contents { get; set; } = new();
    public List<UtmGroupDto> Terms { get; set; } = new();
}

public class UtmGroupDto
{
    public string Key { get; set; } = "";
    public int Leads { get; set; }
    public int Conversions { get; set; }
    public double ConversionRate { get; set; }
    public decimal MockedSpend { get; set; }
    public decimal MockedCpl { get; set; }
    public decimal MockedRoas { get; set; }
}
