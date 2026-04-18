using System.Text.Json.Serialization;

namespace LeadAnalytics.Api.DTOs.Response;

public class DashboardEvolutionDto
{
    [JsonPropertyName("date_from")]
    public DateTime DateFrom { get; set; }

    [JsonPropertyName("date_to")]
    public DateTime DateTo { get; set; }

    [JsonPropertyName("group_by")]
    public string GroupBy { get; set; } = "day";

    public string Compare { get; set; } = "none";

    [JsonPropertyName("total_current")]
    public int TotalCurrent { get; set; }

    [JsonPropertyName("total_compare")]
    public int TotalCompare { get; set; }

    [JsonPropertyName("change_percent")]
    public double? ChangePercent { get; set; }

    public List<DashboardEvolutionPointDto> Current { get; set; } = new();

    public List<DashboardEvolutionPointDto>? Comparison { get; set; }

    [JsonPropertyName("comparison_date_from")]
    public DateTime? ComparisonDateFrom { get; set; }

    [JsonPropertyName("comparison_date_to")]
    public DateTime? ComparisonDateTo { get; set; }
}

public class DashboardEvolutionPointDto
{
    public DateTime Bucket { get; set; }
    public string Label { get; set; } = string.Empty;
    public int Count { get; set; }
}
