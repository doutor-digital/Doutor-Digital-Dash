using System.Globalization;
using System.Text.Json;
using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace LeadAnalytics.Api.Service;

/// <summary>
/// Lê/grava o mapeamento de KPIs por unidade e — o coração da feature — calcula o
/// número de um KPI a partir da sua fonte configurada (etapa da Kommo, campo
/// customizado, ou filtro combinado), tudo direto do nosso banco
/// (Lead.CurrentStageId + Lead.CustomFieldsJson), sem bater na Kommo ao vivo.
/// </summary>
public class KpiConfigService(AppDbContext db)
{
    private readonly AppDbContext _db = db;

    // ─── CRUD ────────────────────────────────────────────────────────────────

    public Task<List<KpiConfiguration>> GetForUnitAsync(int unitId, CancellationToken ct = default) =>
        _db.KpiConfigurations.AsNoTracking()
            .Where(k => k.UnitId == unitId)
            .OrderBy(k => k.KpiKey)
            .ToListAsync(ct);

    /// <summary>Upsert (por KpiKey) de cada mapeamento enviado. Não remove os ausentes.</summary>
    public async Task SaveAsync(
        int unitId, int clinicId,
        IEnumerable<(string KpiKey, string SourceType, string ConfigJson)> items,
        string? email, CancellationToken ct = default)
    {
        var existing = await _db.KpiConfigurations
            .Where(k => k.UnitId == unitId)
            .ToListAsync(ct);

        var now = DateTime.UtcNow;
        foreach (var item in items)
        {
            var row = existing.FirstOrDefault(e => e.KpiKey == item.KpiKey);
            if (row is null)
            {
                _db.KpiConfigurations.Add(new KpiConfiguration
                {
                    UnitId = unitId,
                    ClinicId = clinicId,
                    KpiKey = item.KpiKey,
                    SourceType = item.SourceType,
                    ConfigJson = item.ConfigJson,
                    UpdatedByEmail = email,
                    CreatedAt = now,
                    UpdatedAt = now,
                });
            }
            else
            {
                row.SourceType = item.SourceType;
                row.ConfigJson = item.ConfigJson;
                row.UpdatedByEmail = email;
                row.UpdatedAt = now;
            }
        }

        await _db.SaveChangesAsync(ct);
    }

    // ─── Motor de cálculo ────────────────────────────────────────────────────

    /// <summary>
    /// Calcula o valor de um KPI dado o tipo de fonte e os parâmetros. Retorna o número
    /// e o tamanho da amostra (total de leads do período no escopo unidade/tenant).
    /// </summary>
    public async Task<(double Value, int Sample, string? Note)> ComputeAsync(
        int clinicId, int? unitId, string sourceType, JsonElement config,
        DateTime from, DateTime to, CancellationToken ct = default)
    {
        var baseQuery = _db.Leads.AsNoTracking()
            .Where(l => l.TenantId == clinicId && l.CreatedAt >= from && l.CreatedAt <= to);
        if (unitId.HasValue)
            baseQuery = baseQuery.Where(l => l.UnitId == unitId.Value);

        var sample = await baseQuery.CountAsync(ct);
        var p = ParseConfig(config);

        switch (sourceType)
        {
            case KpiSourceTypes.KommoStage:
            {
                if (p.StageIds.Count == 0)
                    return (0, sample, "Selecione ao menos uma etapa.");
                var ids = p.StageIds;
                var count = await baseQuery
                    .CountAsync(l => l.CurrentStageId != null && ids.Contains(l.CurrentStageId.Value), ct);
                return (count, sample, null);
            }

            case KpiSourceTypes.CustomFieldCount:
            case KpiSourceTypes.CustomFieldSum:
            case KpiSourceTypes.StageFieldFilter:
            {
                if (p.FieldId is null && string.IsNullOrWhiteSpace(p.FieldCode))
                    return (0, sample, "Selecione o campo customizado.");

                var q = baseQuery;
                if (sourceType == KpiSourceTypes.StageFieldFilter && p.StageIds.Count > 0)
                {
                    var ids = p.StageIds;
                    q = q.Where(l => l.CurrentStageId != null && ids.Contains(l.CurrentStageId.Value));
                }

                var rows = await q
                    .Where(l => l.CustomFieldsJson != null)
                    .Select(l => l.CustomFieldsJson!)
                    .ToListAsync(ct);

                if (sourceType == KpiSourceTypes.CustomFieldSum)
                {
                    double sum = 0;
                    foreach (var json in rows)
                    {
                        var v = ExtractFieldValue(json, p.FieldId, p.FieldCode);
                        if (v != null && TryParseNumber(v, out var num)) sum += num;
                    }
                    return (sum, sample, null);
                }

                int matched = 0;
                foreach (var json in rows)
                {
                    var v = ExtractFieldValue(json, p.FieldId, p.FieldCode);
                    if (v is null) continue;
                    if (p.MatchValues.Count == 0) { matched++; continue; } // "campo preenchido"
                    if (p.MatchValues.Any(m => string.Equals(m.Trim(), v.Trim(), StringComparison.OrdinalIgnoreCase)))
                        matched++;
                }
                return (matched, sample, null);
            }

            default:
                return (0, sample, $"Tipo de fonte desconhecido: {sourceType}");
        }
    }

    // ─── Parsing ─────────────────────────────────────────────────────────────

    private record ParsedConfig(List<int> StageIds, long? FieldId, string? FieldCode, List<string> MatchValues);

    private static ParsedConfig ParseConfig(JsonElement config)
    {
        var stageIds = new List<int>();
        long? fieldId = null;
        string? fieldCode = null;
        var matchValues = new List<string>();

        if (config.ValueKind == JsonValueKind.Object)
        {
            if (config.TryGetProperty("stageIds", out var s) && s.ValueKind == JsonValueKind.Array)
                foreach (var el in s.EnumerateArray())
                    if (TryGetInt(el, out var id)) stageIds.Add(id);

            if (config.TryGetProperty("fieldId", out var f) && TryGetLong(f, out var fid))
                fieldId = fid;

            if (config.TryGetProperty("fieldCode", out var fc) && fc.ValueKind == JsonValueKind.String)
                fieldCode = fc.GetString();

            if (config.TryGetProperty("matchValues", out var mv) && mv.ValueKind == JsonValueKind.Array)
                foreach (var el in mv.EnumerateArray())
                    if (el.ValueKind == JsonValueKind.String) matchValues.Add(el.GetString() ?? "");
        }

        return new ParsedConfig(stageIds, fieldId, fieldCode, matchValues);
    }

    /// <summary>Extrai o "value" de um campo do CustomFieldsJson casando por field_id ou field_code.</summary>
    private static string? ExtractFieldValue(string customFieldsJson, long? fieldId, string? fieldCode)
    {
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

                var codeMatch = !string.IsNullOrWhiteSpace(fieldCode)
                    && el.TryGetProperty("field_code", out var fc)
                    && fc.ValueKind == JsonValueKind.String
                    && string.Equals(fc.GetString(), fieldCode, StringComparison.OrdinalIgnoreCase);

                if (idMatch || codeMatch)
                {
                    if (el.TryGetProperty("value", out var val) && val.ValueKind == JsonValueKind.String)
                        return val.GetString();
                    if (el.TryGetProperty("value", out var valN) && valN.ValueKind == JsonValueKind.Number)
                        return valN.GetRawText();
                    return null;
                }
            }
        }
        catch (JsonException) { /* json malformado — ignora */ }
        return null;
    }

    private static bool TryGetInt(JsonElement el, out int value)
    {
        value = 0;
        if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out value)) return true;
        if (el.ValueKind == JsonValueKind.String && int.TryParse(el.GetString(), out value)) return true;
        return false;
    }

    private static bool TryGetLong(JsonElement el, out long value)
    {
        value = 0;
        if (el.ValueKind == JsonValueKind.Number && el.TryGetInt64(out value)) return true;
        if (el.ValueKind == JsonValueKind.String && long.TryParse(el.GetString(), out value)) return true;
        return false;
    }

    /// <summary>Parse tolerante de número (aceita "1.234,56" pt-BR, "1234.56", "R$ 1.200").</summary>
    private static bool TryParseNumber(string raw, out double value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(raw)) return false;

        // mantém só dígitos, separadores e sinal
        var cleaned = new string(raw.Where(c => char.IsDigit(c) || c is '.' or ',' or '-').ToArray());
        if (cleaned.Length == 0) return false;

        var lastComma = cleaned.LastIndexOf(',');
        var lastDot = cleaned.LastIndexOf('.');
        // o separador mais à direita é o decimal; o outro é separador de milhar
        if (lastComma > lastDot)
            cleaned = cleaned.Replace(".", "").Replace(',', '.');
        else
            cleaned = cleaned.Replace(",", "");

        return double.TryParse(cleaned, NumberStyles.Any, CultureInfo.InvariantCulture, out value);
    }
}
