namespace LeadAnalytics.Api.DTOs.Insights;

public class GeoLeadsDto
{
    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }
    public int TotalLeads { get; set; }
    public int LeadsWithGeo { get; set; }
    public List<GeoCityDto> Cities { get; set; } = new();
    public List<GeoStateDto> States { get; set; } = new();
    public List<GeoPointDto> Points { get; set; } = new();
}

public class GeoCityDto
{
    public string City { get; set; } = "";
    public string State { get; set; } = "";
    public int Leads { get; set; }
    public int Conversions { get; set; }
    public double ConversionRate { get; set; }
    public double Lat { get; set; }
    public double Lng { get; set; }
}

public class GeoStateDto
{
    public string State { get; set; } = "";
    public string StateName { get; set; } = "";
    public int Leads { get; set; }
    public int Conversions { get; set; }
}

public class GeoPointDto
{
    public int LeadId { get; set; }
    public string Name { get; set; } = "";
    public string City { get; set; } = "";
    public string State { get; set; } = "";
    public double Lat { get; set; }
    public double Lng { get; set; }
    public bool Converted { get; set; }
}
