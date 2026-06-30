using System.Globalization;
using System.Text.Json;

namespace LeadAnalytics.Api.Service;

/// <summary>
/// Leitor tolerante dos custom fields da Kommo guardados em <c>Lead.CustomFieldsJson</c>
/// (array de <c>{field_id, field_name, field_code, type, value}</c>). Resolve um campo
/// por id, por code ou por predicado sobre o nome, e normaliza o <c>value</c> que pode vir
/// como string, número, ou array de enums (<c>[{"value":"X"}]</c> ou <c>["X"]</c>).
///
/// Utilitário compartilhado para que módulos novos (ex.: dashboard jurídico) leiam os
/// mesmos dados sem reimplementar o parsing que vive em <see cref="KpiConfigService"/>.
/// </summary>
public static class CustomFieldReader
{
    /// <summary>Valor textual do campo identificado por <paramref name="fieldId"/> (ou, na falta dele, pelo nome).</summary>
    public static string? Read(string? customFieldsJson, long? fieldId, Func<string, bool>? nameMatches = null)
    {
        if (string.IsNullOrWhiteSpace(customFieldsJson)) return null;
        try
        {
            using var doc = JsonDocument.Parse(customFieldsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;

            foreach (var el in doc.RootElement.EnumerateArray())
            {
                if (el.ValueKind != JsonValueKind.Object) continue;

                var idMatch = fieldId is not null
                    && el.TryGetProperty("field_id", out var fid)
                    && TryGetLong(fid, out var idv) && idv == fieldId.Value;

                var nameMatch = !idMatch && fieldId is null && nameMatches is not null
                    && el.TryGetProperty("field_name", out var fn)
                    && fn.ValueKind == JsonValueKind.String
                    && fn.GetString() is { } name
                    && nameMatches(name.ToLowerInvariant());

                if (idMatch || nameMatch)
                    return el.TryGetProperty("value", out var val) ? NormalizeValue(val) : null;
            }
        }
        catch (JsonException) { /* json malformado — ignora */ }
        return null;
    }

    /// <summary>Lê o campo e tenta convertê-lo em decimal (aceita "1.500,00" pt-BR e "1500.00").</summary>
    public static decimal? ReadDecimal(string? customFieldsJson, long? fieldId, Func<string, bool>? nameMatches = null)
        => TryParseDecimal(Read(customFieldsJson, fieldId, nameMatches));

    /// <summary>true quando o campo está preenchido com algum valor não-vazio.</summary>
    public static bool IsFilled(string? customFieldsJson, long? fieldId, Func<string, bool>? nameMatches = null)
        => !string.IsNullOrWhiteSpace(Read(customFieldsJson, fieldId, nameMatches));

    /// <summary>true quando o valor do campo bate (case-insensitive, trim) com algum de <paramref name="values"/>.</summary>
    public static bool Matches(string? customFieldsJson, long? fieldId, IEnumerable<string> values, Func<string, bool>? nameMatches = null)
    {
        var v = Read(customFieldsJson, fieldId, nameMatches)?.Trim();
        if (string.IsNullOrEmpty(v)) return false;
        return values.Any(m => string.Equals(m.Trim(), v, StringComparison.OrdinalIgnoreCase));
    }

    private static string? NormalizeValue(JsonElement val) => val.ValueKind switch
    {
        JsonValueKind.String => val.GetString(),
        JsonValueKind.Number => val.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Array => FirstOfArray(val),
        _ => null,
    };

    private static string? FirstOfArray(JsonElement arr)
    {
        foreach (var item in arr.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String) return item.GetString();
            if (item.ValueKind == JsonValueKind.Number) return item.GetRawText();
            if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty("value", out var v))
                return NormalizeValue(v);
        }
        return null;
    }

    private static decimal? TryParseDecimal(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var s = raw.Trim();
        if (decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var v1)) return v1;
        if (decimal.TryParse(s, NumberStyles.Any, new CultureInfo("pt-BR"), out var v2)) return v2;
        // último recurso: limpa "R$", milhares e normaliza separador decimal
        var cleaned = new string(s.Where(c => char.IsDigit(c) || c is '.' or ',' or '-').ToArray());
        if (cleaned.Length == 0) return null;
        var lastComma = cleaned.LastIndexOf(',');
        var lastDot = cleaned.LastIndexOf('.');
        cleaned = lastComma > lastDot ? cleaned.Replace(".", "").Replace(',', '.') : cleaned.Replace(",", "");
        return decimal.TryParse(cleaned, NumberStyles.Any, CultureInfo.InvariantCulture, out var v3) ? v3 : null;
    }

    private static bool TryGetLong(JsonElement el, out long value)
    {
        value = 0;
        if (el.ValueKind == JsonValueKind.Number && el.TryGetInt64(out value)) return true;
        return el.ValueKind == JsonValueKind.String && long.TryParse(el.GetString(), out value);
    }
}
