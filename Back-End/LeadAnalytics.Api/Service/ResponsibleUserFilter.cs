using System.Globalization;
using System.Text;
using System.Text.Json;
using LeadAnalytics.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace LeadAnalytics.Api.Service;

/// <summary>
/// Resolve e filtra leads pelo SDR responsável guardado no campo customizado
/// "Usuário responsável" da Kommo. A conta tem um único login Kommo para todas as
/// SDRs, então o <c>responsible_user_id</c> é sempre o mesmo — quem realmente
/// atendeu vem desse custom field. O match do JSON é feito em memória (o EF não
/// consulta dentro do CustomFieldsJson).
/// </summary>
public static class ResponsibleUserFilter
{
    /// <summary>Nome do campo (normalizado: sem acento, minúsculo) que guarda o responsável.</summary>
    public const string FieldNameNormalized = "usuario responsavel";

    /// <summary>Minúsculo + sem acentos, para comparar nomes de campo/valor de forma robusta.</summary>
    public static string? Normalize(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var formD = s.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(formD.Length);
        foreach (var ch in formD)
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                sb.Append(ch);
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    /// <summary>Extrai o valor do campo "Usuário responsável" de um CustomFieldsJson.</summary>
    public static string? Extract(string? customFieldsJson)
    {
        if (string.IsNullOrWhiteSpace(customFieldsJson)) return null;
        try
        {
            using var doc = JsonDocument.Parse(customFieldsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                if (el.ValueKind != JsonValueKind.Object) continue;
                if (!el.TryGetProperty("field_name", out var fn) || fn.ValueKind != JsonValueKind.String) continue;
                if (Normalize(fn.GetString()) != FieldNameNormalized) continue;

                if (el.TryGetProperty("value", out var val) && val.ValueKind == JsonValueKind.String)
                {
                    var v = val.GetString();
                    return string.IsNullOrWhiteSpace(v) ? null : v.Trim();
                }
                return null;
            }
        }
        catch (JsonException) { /* json malformado — ignora */ }
        return null;
    }

    /// <summary>
    /// Restringe a query aos leads cujo campo "Usuário responsável" casa com
    /// <paramref name="responsibleUser"/> (sem acento/maiúsculas). Faz a varredura em
    /// memória dentro da janela já filtrada (tenant/unidade/período) e devolve a query
    /// limitada por Id — preservando a IQueryable para as agregações seguintes.
    /// </summary>
    public static async Task<IQueryable<Lead>> ApplyAsync(
        IQueryable<Lead> query, string? responsibleUser, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(responsibleUser)) return query;

        var target = Normalize(responsibleUser);
        var rows = await query
            .Where(l => l.CustomFieldsJson != null)
            .Select(l => new { l.Id, l.CustomFieldsJson })
            .ToListAsync(ct);

        var ids = rows
            .Where(x => Normalize(Extract(x.CustomFieldsJson)) == target)
            .Select(x => x.Id)
            .ToList();

        return query.Where(l => ids.Contains(l.Id));
    }
}
