using System.Text.Json;

namespace LeadAnalytics.Api.Service;

/// <summary>
/// De onde o lead veio — lido do cartão da Kommo, não da coluna <c>leads.Source</c>.
///
/// POR QUE ESTA CLASSE EXISTE
/// --------------------------
/// A coluna <c>Source</c> guarda o CANAL DE ENTRADA no nosso sistema, e hoje ela vale
/// "Kommo" em 100% dos leads — 6.447 de 6.447 nos últimos 30 dias. Quem a usasse como
/// dimensão de origem via a tela dizer "Top origens: Kommo 15", e o filtro de origem do
/// dashboard oferecer uma opção só. Não é um número errado, é uma pergunta respondida com
/// a resposta de outra.
///
/// A origem de verdade mora no campo do cartão, que aparece como "⚑ Origem" e, em base
/// antiga, como "Origem": Meta-Instagram, Meta-Facebook, Org-Facebook, Indicação, Fachada,
/// Site oficial - Franquia, Google, Rádio, Outdoor.
///
/// POR QUE O NOME DO CAMPO É COMPARADO ASSIM
/// -----------------------------------------
/// Existem outros três campos que terminam em "origem" — "Canal de origem",
/// "⌂ Plataforma de origem" e "⌂ URL de origem do clique". Comparar pelo fim do nome
/// pegaria os três. Por isso o símbolo é removido e o resto tem de ser exatamente "origem".
/// </summary>
public static class OrigemDoLead
{
    /// <summary>Os símbolos que a Kommo usa como prefixo de grupo no nome do campo.</summary>
    private static readonly char[] Simbolos = ['⚑', '⌂', '☎', ' ', '\t'];

    /// <summary>O campo de origem, e só ele. Recebe o nome já em minúsculas.</summary>
    public static bool EhCampoOrigem(string nomeMinusculo)
        => nomeMinusculo.TrimStart(Simbolos) == "origem";

    /// <summary>
    /// A origem gravada no cartão, ou null quando o cartão não tem o campo preenchido.
    /// Cartão com JSON torto devolve null em vez de derrubar quem chamou.
    /// </summary>
    public static string? Ler(string? customFieldsJson)
    {
        if (string.IsNullOrWhiteSpace(customFieldsJson)) return null;

        try
        {
            using var doc = JsonDocument.Parse(customFieldsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;

            foreach (var f in doc.RootElement.EnumerateArray())
            {
                if (!f.TryGetProperty("field_name", out var n) || n.ValueKind != JsonValueKind.String) continue;
                if (!EhCampoOrigem((n.GetString() ?? "").ToLowerInvariant())) continue;
                if (!f.TryGetProperty("value", out var v)) continue;

                var valor = v.ValueKind == JsonValueKind.String ? v.GetString() : v.ToString();
                return string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();
            }
        }
        catch (JsonException) { /* cartão torto não conta, e não derruba a tela */ }

        return null;
    }
}
