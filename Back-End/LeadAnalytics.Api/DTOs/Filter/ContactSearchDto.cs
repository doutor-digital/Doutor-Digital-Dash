using System.Text.Json;
using System.Text.Json.Serialization;
using LeadAnalytics.Api.DTOs.Response;

namespace LeadAnalytics.Api.DTOs.Filter;

public class ContactSearchRequestDto
{
    public List<FilterCriterionDto>? Filters { get; set; }

    public string Origem { get; set; } = "all";
    public string? Search { get; set; }

    public int Page { get; set; } = 1;

    [JsonPropertyName("page_size")]
    public int PageSize { get; set; } = 50;

    [JsonPropertyName("order_by")]
    public string OrderBy { get; set; } = "last_message_at";

    [JsonPropertyName("order_dir")]
    public string OrderDir { get; set; } = "desc";
}

public class FilterCriterionDto
{
    public string Field { get; set; } = string.Empty;
    public string Op { get; set; } = string.Empty;
    public JsonElement Value { get; set; }
}

public class ContactSearchResponseDto
{
    public List<ContactDto> Data { get; set; } = new();
    public ContactPaginationDto Pagination { get; set; } = new();
    public ContactSearchCountsDto Counts { get; set; } = new();
}

public class ContactSearchCountsDto
{
    public int All { get; set; }

    [JsonPropertyName("webhook_cloudia")]
    public int WebhookCloudia { get; set; }

    [JsonPropertyName("import_csv")]
    public int ImportCsv { get; set; }

    public int Manual { get; set; }

    public int Filtered { get; set; }
}

public class FilterOptionsResponseDto
{
    public string Key { get; set; } = string.Empty;
    public List<FilterOptionDto> Options { get; set; } = new();
}

public class FilterOptionDto
{
    public string Value { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
}
