using System.Text.Json.Serialization;

namespace LeadAnalytics.Api.DTOs.Response;

public class ContactDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("phone_normalized")]
    public string PhoneNormalized { get; set; } = string.Empty;

    public string Origem { get; set; } = "webhook_cloudia";
    public string? Etapa { get; set; }
    public List<string> Tags { get; set; } = new();

    [JsonPropertyName("last_message_at")]
    public DateTime? LastMessageAt { get; set; }

    public bool Blocked { get; set; }

    [JsonPropertyName("imported_at")]
    public DateTime? ImportedAt { get; set; }
}

public class ContactsListResponseDto
{
    public List<ContactDto> Data { get; set; } = new();
    public ContactPaginationDto Pagination { get; set; } = new();
    public ContactCountsDto Counts { get; set; } = new();
}

public class ContactPaginationDto
{
    public int Page { get; set; }

    [JsonPropertyName("page_size")]
    public int PageSize { get; set; }

    public int Total { get; set; }

    [JsonPropertyName("total_pages")]
    public int TotalPages { get; set; }
}

public class ContactCountsDto
{
    public int All { get; set; }

    [JsonPropertyName("webhook_cloudia")]
    public int WebhookCloudia { get; set; }

    [JsonPropertyName("import_csv")]
    public int ImportCsv { get; set; }
}

public class ContactImportResultDto
{
    [JsonPropertyName("batch_id")]
    public int BatchId { get; set; }

    public string Filename { get; set; } = string.Empty;

    [JsonPropertyName("total_rows")]
    public int TotalRows { get; set; }

    public int Created { get; set; }
    public int Updated { get; set; }
    public int Skipped { get; set; }
    public int Errors { get; set; }

    [JsonPropertyName("error_samples")]
    public List<ContactImportErrorDto> ErrorSamples { get; set; } = new();
}

public class ContactImportErrorDto
{
    public int Row { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string? Value { get; set; }
}
