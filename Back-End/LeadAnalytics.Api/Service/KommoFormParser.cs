using System.Text.Json;
using LeadAnalytics.Api.DTOs.Kommo;
using Microsoft.AspNetCore.Http;

namespace LeadAnalytics.Api.Service;

/// <summary>
/// Converte o corpo <c>application/x-www-form-urlencoded</c> de um webhook do Kommo
/// (com chaves em notação de colchetes, ex.: <c>leads[add][0][custom_fields][0][id]=77</c>)
/// numa árvore aninhada e, daí, num <see cref="KommoWebhookPayload"/> fortemente tipado.
///
/// Estratégia:
///   1. Cada chave é quebrada em segmentos: <c>leads[add][0][id]</c> → ["leads","add","0","id"].
///   2. Os segmentos são inseridos numa árvore de <see cref="Dictionary{TKey,TValue}"/>.
///   3. Dicionários cujas chaves são índices inteiros sequenciais (0,1,2,…) viram listas.
///   4. A árvore é serializada para JSON e desserializada no tipo final.
/// </summary>
public static class KommoFormParser
{
    public static KommoWebhookPayload Parse(IFormCollection form)
    {
        var root = new Dictionary<string, object?>();

        foreach (var (key, value) in form)
        {
            var segments = SplitKey(key);
            if (segments.Count == 0) continue;
            // O Kommo manda uma única string por chave; pegamos a primeira.
            Insert(root, segments, value.Count > 0 ? value[0] : null);
        }

        var normalized = Normalize(root);
        var json = JsonSerializer.Serialize(normalized);

        return JsonSerializer.Deserialize<KommoWebhookPayload>(json) ?? new KommoWebhookPayload();
    }

    /// <summary>Quebra "leads[add][0][id]" em ["leads", "add", "0", "id"].</summary>
    private static List<string> SplitKey(string key)
    {
        var segments = new List<string>();
        var current = new System.Text.StringBuilder();

        foreach (var ch in key)
        {
            if (ch == '[')
            {
                if (current.Length > 0) { segments.Add(current.ToString()); current.Clear(); }
            }
            else if (ch == ']')
            {
                if (current.Length > 0) { segments.Add(current.ToString()); current.Clear(); }
            }
            else
            {
                current.Append(ch);
            }
        }

        if (current.Length > 0) segments.Add(current.ToString());
        return segments;
    }

    private static void Insert(Dictionary<string, object?> node, List<string> segments, string? value)
    {
        for (var i = 0; i < segments.Count; i++)
        {
            var seg = segments[i];
            var isLast = i == segments.Count - 1;

            if (isLast)
            {
                node[seg] = value;
                return;
            }

            if (!node.TryGetValue(seg, out var child) || child is not Dictionary<string, object?> childDict)
            {
                childDict = new Dictionary<string, object?>();
                node[seg] = childDict;
            }

            node = childDict;
        }
    }

    /// <summary>
    /// Converte recursivamente dicionários "tipo array" (chaves 0,1,2,… contíguas) em listas.
    /// </summary>
    private static object? Normalize(object? value)
    {
        if (value is not Dictionary<string, object?> dict)
            return value;

        var normalizedChildren = new Dictionary<string, object?>(dict.Count);
        foreach (var (k, v) in dict)
            normalizedChildren[k] = Normalize(v);

        if (IsSequentialIntegerKeyed(normalizedChildren))
        {
            return normalizedChildren
                .OrderBy(kv => int.Parse(kv.Key))
                .Select(kv => kv.Value)
                .ToList();
        }

        return normalizedChildren;
    }

    private static bool IsSequentialIntegerKeyed(Dictionary<string, object?> dict)
    {
        if (dict.Count == 0) return false;

        var indices = new List<int>(dict.Count);
        foreach (var key in dict.Keys)
        {
            if (!int.TryParse(key, out var idx)) return false;
            indices.Add(idx);
        }

        indices.Sort();
        for (var i = 0; i < indices.Count; i++)
            if (indices[i] != i) return false;

        return true;
    }
}
