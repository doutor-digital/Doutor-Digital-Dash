using System.Globalization;
using System.Text.Json;

namespace LeadAnalytics.Api.Service.Filtering;

/// <summary>
/// Lê o campo `value` do critério aplicando coerção estrita conforme o tipo
/// esperado. Rejeita formatos inválidos com <see cref="FilterValidationException"/>.
/// </summary>
public sealed class ValueReader
{
    public JsonElement Raw { get; }
    public string FieldId { get; }
    public int MaxInItems { get; }

    public ValueReader(JsonElement raw, string fieldId, int maxInItems)
    {
        Raw = raw;
        FieldId = fieldId;
        MaxInItems = maxInItems;
    }

    public string ReadString()
    {
        if (Raw.ValueKind == JsonValueKind.String)
        {
            var s = Raw.GetString() ?? "";
            if (string.IsNullOrWhiteSpace(s))
                throw new FilterValidationException($"valor vazio para '{FieldId}'", FieldId);
            return s;
        }
        if (Raw.ValueKind == JsonValueKind.Number) return Raw.GetRawText();
        throw new FilterValidationException($"valor esperado string para '{FieldId}'", FieldId);
    }

    public int ReadPositiveInt()
    {
        int n;
        if (Raw.ValueKind == JsonValueKind.Number)
        {
            if (!Raw.TryGetInt32(out n))
                throw new FilterValidationException($"valor inteiro esperado para '{FieldId}'", FieldId);
        }
        else if (Raw.ValueKind == JsonValueKind.String &&
                 int.TryParse(Raw.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            n = parsed;
        }
        else
        {
            throw new FilterValidationException($"valor inteiro esperado para '{FieldId}'", FieldId);
        }
        if (n <= 0) throw new FilterValidationException($"valor deve ser > 0 para '{FieldId}'", FieldId);
        return n;
    }

    public decimal ReadDecimal()
    {
        if (Raw.ValueKind == JsonValueKind.Number && Raw.TryGetDecimal(out var d)) return d;
        if (Raw.ValueKind == JsonValueKind.String &&
            decimal.TryParse(Raw.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var p)) return p;
        throw new FilterValidationException($"valor numérico esperado para '{FieldId}'", FieldId);
    }

    public (decimal a, decimal b) ReadDecimalRange()
    {
        if (Raw.ValueKind != JsonValueKind.Array || Raw.GetArrayLength() != 2)
            throw new FilterValidationException($"valor esperado [min, max] para '{FieldId}'", FieldId);
        var arr = Raw.EnumerateArray().ToArray();
        decimal Read(JsonElement el) =>
            el.ValueKind == JsonValueKind.Number && el.TryGetDecimal(out var d) ? d :
            el.ValueKind == JsonValueKind.String && decimal.TryParse(el.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var p) ? p :
            throw new FilterValidationException($"componente não-numérico em '{FieldId}'", FieldId);
        var a = Read(arr[0]); var b = Read(arr[1]);
        if (a > b) (a, b) = (b, a);
        return (a, b);
    }

    public DateTime ReadDate()
    {
        if (Raw.ValueKind != JsonValueKind.String)
            throw new FilterValidationException($"data esperada em formato ISO (YYYY-MM-DD) para '{FieldId}'", FieldId);
        var s = Raw.GetString();
        if (string.IsNullOrWhiteSpace(s))
            throw new FilterValidationException($"data vazia para '{FieldId}'", FieldId);
        if (DateTime.TryParse(s, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dt))
            return DateTime.SpecifyKind(dt, DateTimeKind.Utc);
        throw new FilterValidationException($"data inválida '{s}' para '{FieldId}'", FieldId);
    }

    public List<string> ReadStringList()
    {
        if (Raw.ValueKind != JsonValueKind.Array)
            throw new FilterValidationException($"lista esperada para '{FieldId}'", FieldId);

        var len = Raw.GetArrayLength();
        if (len > MaxInItems)
            throw new FilterValidationException(
                $"lista com {len} itens — máximo permitido é {MaxInItems} para '{FieldId}'", FieldId);

        var list = new List<string>(len);
        foreach (var el in Raw.EnumerateArray())
        {
            if (el.ValueKind != JsonValueKind.String)
                throw new FilterValidationException($"todos os itens devem ser string em '{FieldId}'", FieldId);
            var s = el.GetString();
            if (!string.IsNullOrWhiteSpace(s)) list.Add(s);
        }
        return list;
    }
}
