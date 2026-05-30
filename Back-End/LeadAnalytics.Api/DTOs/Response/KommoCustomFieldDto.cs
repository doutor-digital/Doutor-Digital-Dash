using System.Text.Json.Serialization;

namespace LeadAnalytics.Api.DTOs.Response;

/// <summary>
/// Definição de um custom field cadastrado nos leads da Kommo, devolvido pelo
/// endpoint GET /units/{id}/kommo-custom-fields. O front usa o array `enums`
/// (quando type=select/multiselect) pra montar dropdowns dinâmicos no filtro.
/// </summary>
public class KommoCustomFieldDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>"text" | "numeric" | "checkbox" | "select" | "multiselect" | "date" | "url" | "textarea" | "radiobutton" | "streetaddress" | "smart_address" | "birthday" | "legal_entity" | "date_time" | "price" | "category" | "items" | "tracking_data" | "linked_entity" | "chained_list" | "monetary" | "file" | "payer" | "supplier"</summary>
    public string Type { get; set; } = "text";

    public string? Code { get; set; }

    [JsonPropertyName("is_api_only")]
    public bool IsApiOnly { get; set; }

    /// <summary>Opções (quando type=select/multiselect/radiobutton/category).</summary>
    public List<KommoCustomFieldEnumDto> Enums { get; set; } = new();
}

public class KommoCustomFieldEnumDto
{
    public long Id { get; set; }
    public string Value { get; set; } = string.Empty;
    public string? Code { get; set; }
}
