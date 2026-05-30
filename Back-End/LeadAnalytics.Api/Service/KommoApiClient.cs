using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LeadAnalytics.Api.Service;

/// <summary>
/// Cliente tipado pra REST API v4 da Kommo. Usado pelo
/// <see cref="KommoSyncService"/> pra puxar leads/contatos existentes da
/// conta da unidade (sincronização inicial).
///
/// Auth: Bearer token de longa duração (Perfil → Integrações → API).
/// Base URL: https://{subdomain}.kommo.com/api/v4/
/// </summary>
public class KommoApiClient
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _http;
    private readonly ILogger<KommoApiClient> _logger;

    public KommoApiClient(HttpClient http, ILogger<KommoApiClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    /// <summary>Normaliza "araguainadoutorhernia.kommo.com" → "araguainadoutorhernia.kommo.com" e gera a base.</summary>
    public static string ResolveBaseUrl(string subdomainOrHost)
    {
        var s = subdomainOrHost.Trim().TrimEnd('/');
        if (!s.Contains('.', StringComparison.Ordinal)) s = $"{s}.kommo.com";
        if (!s.StartsWith("http", StringComparison.OrdinalIgnoreCase)) s = $"https://{s}";
        return s;
    }

    public async Task<KommoLeadsPageResponse?> GetLeadsPageAsync(
        string subdomainOrHost, string token, int page, int limit, CancellationToken ct)
    {
        var url = $"{ResolveBaseUrl(subdomainOrHost)}/api/v4/leads?limit={limit}&page={page}";
        return await GetAsync<KommoLeadsPageResponse>(url, token, ct);
    }

    public async Task<KommoContactsPageResponse?> GetContactsByIdsAsync(
        string subdomainOrHost, string token, IEnumerable<long> ids, CancellationToken ct)
    {
        var idList = ids.ToList();
        if (idList.Count == 0) return new KommoContactsPageResponse();

        var qs = string.Join("&", idList.Select(id => $"filter[id][]={id}"));
        var url = $"{ResolveBaseUrl(subdomainOrHost)}/api/v4/contacts?{qs}&limit=250";
        return await GetAsync<KommoContactsPageResponse>(url, token, ct);
    }

    public async Task<KommoAccountResponse?> GetAccountAsync(string subdomainOrHost, string token, CancellationToken ct)
    {
        var url = $"{ResolveBaseUrl(subdomainOrHost)}/api/v4/account";
        return await GetAsync<KommoAccountResponse>(url, token, ct);
    }

    /// <summary>
    /// Lista todos os pipelines da conta com seus status. Usado pelo dashboard
    /// para traduzir status_id em nome amigável da etapa.
    /// </summary>
    public async Task<KommoPipelinesResponse?> GetPipelinesAsync(string subdomainOrHost, string token, CancellationToken ct)
    {
        var url = $"{ResolveBaseUrl(subdomainOrHost)}/api/v4/leads/pipelines";
        return await GetAsync<KommoPipelinesResponse>(url, token, ct);
    }

    private async Task<T?> GetAsync<T>(string url, string token, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);

        if (resp.StatusCode == System.Net.HttpStatusCode.NoContent) return default;

        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(ct);
            _logger.LogWarning("Kommo API {Status} em {Url}: {Body}", (int)resp.StatusCode, url, body);
            throw new HttpRequestException($"Kommo API retornou {(int)resp.StatusCode}: {body}");
        }

        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOpts, ct);
    }
}

// ─── Tipos de resposta ────────────────────────────────────────────────────

public class KommoLeadsPageResponse
{
    [JsonPropertyName("_page")] public int Page { get; set; }
    [JsonPropertyName("_links")] public KommoLinks? Links { get; set; }
    [JsonPropertyName("_embedded")] public KommoLeadsEmbedded? Embedded { get; set; }
}

public class KommoLeadsEmbedded
{
    [JsonPropertyName("leads")] public List<KommoApiLead>? Leads { get; set; }
}

public class KommoApiLead
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("price")] public long? Price { get; set; }
    [JsonPropertyName("status_id")] public long? StatusId { get; set; }
    [JsonPropertyName("pipeline_id")] public long? PipelineId { get; set; }
    [JsonPropertyName("responsible_user_id")] public long? ResponsibleUserId { get; set; }
    [JsonPropertyName("account_id")] public long? AccountId { get; set; }
    [JsonPropertyName("created_at")] public long? CreatedAt { get; set; }
    [JsonPropertyName("updated_at")] public long? UpdatedAt { get; set; }
    [JsonPropertyName("is_deleted")] public bool IsDeleted { get; set; }
    [JsonPropertyName("custom_fields_values")] public List<KommoApiCustomField>? CustomFieldsValues { get; set; }
    [JsonPropertyName("_embedded")] public KommoApiLeadEmbedded? Embedded { get; set; }
}

public class KommoApiLeadEmbedded
{
    [JsonPropertyName("contacts")] public List<KommoApiContactRef>? Contacts { get; set; }
    [JsonPropertyName("tags")] public List<KommoApiTag>? Tags { get; set; }
}

public class KommoApiContactRef
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("is_main")] public bool IsMain { get; set; }
}

public class KommoApiTag
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
}

public class KommoApiCustomField
{
    [JsonPropertyName("field_id")] public long FieldId { get; set; }
    [JsonPropertyName("field_name")] public string? FieldName { get; set; }
    [JsonPropertyName("field_code")] public string? FieldCode { get; set; }
    [JsonPropertyName("field_type")] public string? FieldType { get; set; }
    [JsonPropertyName("values")] public List<KommoApiCustomFieldValue>? Values { get; set; }
}

public class KommoApiCustomFieldValue
{
    [JsonPropertyName("value")] public JsonElement? Value { get; set; }
    [JsonPropertyName("enum_code")] public string? EnumCode { get; set; }
    [JsonPropertyName("enum_id")] public long? EnumId { get; set; }

    public string? GetStringValue()
    {
        if (Value is not { } v) return null;
        return v.ValueKind switch
        {
            JsonValueKind.String => v.GetString(),
            JsonValueKind.Number => v.ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => v.ToString(),
        };
    }
}

public class KommoContactsPageResponse
{
    [JsonPropertyName("_page")] public int Page { get; set; }
    [JsonPropertyName("_links")] public KommoLinks? Links { get; set; }
    [JsonPropertyName("_embedded")] public KommoContactsEmbedded? Embedded { get; set; }
}

public class KommoContactsEmbedded
{
    [JsonPropertyName("contacts")] public List<KommoApiContact>? Contacts { get; set; }
}

public class KommoApiContact
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("first_name")] public string? FirstName { get; set; }
    [JsonPropertyName("last_name")] public string? LastName { get; set; }
    [JsonPropertyName("custom_fields_values")] public List<KommoApiCustomField>? CustomFieldsValues { get; set; }
}

public class KommoAccountResponse
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("subdomain")] public string? Subdomain { get; set; }
}

public class KommoPipelinesResponse
{
    [JsonPropertyName("_total_items")] public int TotalItems { get; set; }
    [JsonPropertyName("_embedded")] public KommoPipelinesEmbedded? Embedded { get; set; }
}

public class KommoPipelinesEmbedded
{
    [JsonPropertyName("pipelines")] public List<KommoApiPipeline>? Pipelines { get; set; }
}

public class KommoApiPipeline
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("sort")] public int Sort { get; set; }
    [JsonPropertyName("is_main")] public bool IsMain { get; set; }
    [JsonPropertyName("is_archive")] public bool IsArchive { get; set; }
    [JsonPropertyName("_embedded")] public KommoApiPipelineEmbedded? Embedded { get; set; }
}

public class KommoApiPipelineEmbedded
{
    [JsonPropertyName("statuses")] public List<KommoApiStatus>? Statuses { get; set; }
}

public class KommoApiStatus
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("sort")] public int Sort { get; set; }
    [JsonPropertyName("color")] public string? Color { get; set; }
    [JsonPropertyName("type")] public int Type { get; set; }
    [JsonPropertyName("pipeline_id")] public long PipelineId { get; set; }
}

public class KommoLinks
{
    [JsonPropertyName("self")] public KommoLinkHref? Self { get; set; }
    [JsonPropertyName("next")] public KommoLinkHref? Next { get; set; }
    [JsonPropertyName("first")] public KommoLinkHref? First { get; set; }
    [JsonPropertyName("prev")] public KommoLinkHref? Prev { get; set; }
}

public class KommoLinkHref
{
    [JsonPropertyName("href")] public string? Href { get; set; }
}
