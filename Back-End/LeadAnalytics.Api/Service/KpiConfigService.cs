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
        IEnumerable<KpiSaveItem> items,
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
                    IsCustom = item.IsCustom,
                    DisplayName = item.DisplayName,
                    AccentColor = item.AccentColor,
                    DisplayType = item.DisplayType,
                    SortOrder = item.SortOrder,
                    UpdatedByEmail = email,
                    CreatedAt = now,
                    UpdatedAt = now,
                });
            }
            else
            {
                row.SourceType = item.SourceType;
                row.ConfigJson = item.ConfigJson;
                row.IsCustom = item.IsCustom;
                row.DisplayName = item.DisplayName;
                row.AccentColor = item.AccentColor;
                row.DisplayType = item.DisplayType;
                row.SortOrder = item.SortOrder;
                row.UpdatedByEmail = email;
                row.UpdatedAt = now;
            }
        }

        await _db.SaveChangesAsync(ct);
    }

    /// <summary>Remove um KPI (usado para apagar KPIs custom). No-op se não existir.</summary>
    public async Task<bool> DeleteAsync(int unitId, string kpiKey, CancellationToken ct = default)
    {
        var row = await _db.KpiConfigurations
            .FirstOrDefaultAsync(k => k.UnitId == unitId && k.KpiKey == kpiKey, ct);
        if (row is null) return false;
        _db.KpiConfigurations.Remove(row);
        await _db.SaveChangesAsync(ct);
        return true;
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
            case KpiSourceTypes.CreatedInPeriod:
                // Todos os leads criados no período (ex.: "Total de Leads").
                return (sample, sample, null);

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

    /// <summary>
    /// Drill-down: devolve os leads por trás de um KPI (mesma lógica de filtro do
    /// ComputeAsync, mas retornando os leads em vez do número). Cap em <paramref name="limit"/>.
    /// </summary>
    public async Task<(List<DTOs.Kpi.KpiLeadDto> Items, int Total, bool Truncated)> ComputeLeadsAsync(
        int clinicId, int? unitId, string sourceType, JsonElement config,
        DateTime from, DateTime to, int limit = 500, CancellationToken ct = default)
    {
        const int MaxScan = 5000; // teto de varredura p/ filtro em memória

        var q = _db.Leads.AsNoTracking()
            .Where(l => l.TenantId == clinicId && l.CreatedAt >= from && l.CreatedAt <= to);
        if (unitId.HasValue)
            q = q.Where(l => l.UnitId == unitId.Value);

        var p = ParseConfig(config);
        var fieldBased = sourceType is KpiSourceTypes.CustomFieldCount
            or KpiSourceTypes.CustomFieldSum or KpiSourceTypes.StageFieldFilter;

        // Filtro por etapa (em SQL).
        if (sourceType == KpiSourceTypes.KommoStage)
        {
            if (p.StageIds.Count == 0) return (new(), 0, false);
            var ids = p.StageIds;
            q = q.Where(l => l.CurrentStageId != null && ids.Contains(l.CurrentStageId.Value));
        }
        else if (sourceType == KpiSourceTypes.StageFieldFilter && p.StageIds.Count > 0)
        {
            var ids = p.StageIds;
            q = q.Where(l => l.CurrentStageId != null && ids.Contains(l.CurrentStageId.Value));
        }

        q = q.OrderByDescending(l => l.CreatedAt);

        var hits = new List<DTOs.Kpi.KpiLeadDto>();
        var scanned = 0;
        var truncated = false;

        var rows = await q.Take(MaxScan).Select(l => new
        {
            l.Id, l.ExternalId, l.Name, l.Phone, l.Source, l.Channel,
            l.CurrentStage, l.CurrentStageId, l.LeadType, l.HasAppointment, l.HasPayment,
            l.CreatedAt, l.CustomFieldsJson,
        }).ToListAsync(ct);

        truncated = rows.Count >= MaxScan;

        foreach (var l in rows)
        {
            scanned++;
            string? matched = null;

            if (fieldBased)
            {
                matched = ExtractFieldValue(l.CustomFieldsJson ?? "[]", p.FieldId, p.FieldCode);
                if (matched is null) continue; // campo não preenchido
                if (sourceType != KpiSourceTypes.CustomFieldSum && p.MatchValues.Count > 0 &&
                    !p.MatchValues.Any(m => string.Equals(m.Trim(), matched.Trim(), StringComparison.OrdinalIgnoreCase)))
                    continue;
            }

            hits.Add(new DTOs.Kpi.KpiLeadDto
            {
                Id = l.Id,
                ExternalId = l.ExternalId,
                Name = l.Name,
                Phone = l.Phone,
                Source = l.Source,
                Channel = l.Channel,
                CurrentStage = l.CurrentStage,
                CurrentStageId = l.CurrentStageId,
                LeadType = l.LeadType,
                HasAppointment = l.HasAppointment,
                HasPayment = l.HasPayment,
                CreatedAt = l.CreatedAt,
                MatchedValue = matched,
            });

            if (hits.Count >= limit) { truncated = true; break; }
        }

        return (hits, hits.Count, truncated);
    }

    /// <summary>
    /// Métricas de TODOS os campos customizados dos leads do período: para cada campo,
    /// quantos leads o preenchem e a distribuição dos valores mais comuns. É o dado do
    /// dashboard "perfil do lead".
    /// </summary>
    public async Task<(int TotalLeads, List<DTOs.Kpi.CustomFieldSummaryDto> Fields, bool Truncated)>
        CustomFieldsSummaryAsync(int clinicId, int? unitId, DateTime from, DateTime to,
            int topValues = 8, CancellationToken ct = default)
    {
        const int MaxScan = 8000;

        var q = _db.Leads.AsNoTracking()
            .Where(l => l.TenantId == clinicId && l.CreatedAt >= from && l.CreatedAt <= to
                        && l.CustomFieldsJson != null);
        if (unitId.HasValue)
            q = q.Where(l => l.UnitId == unitId.Value);

        var total = await q.CountAsync(ct);
        var jsons = await q.OrderByDescending(l => l.CreatedAt)
            .Take(MaxScan).Select(l => l.CustomFieldsJson!).ToListAsync(ct);
        var truncated = jsons.Count >= MaxScan;

        // field_id -> agregador
        var agg = new Dictionary<long, FieldAgg>();
        foreach (var json in jsons)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind != JsonValueKind.Array) continue;
                foreach (var el in doc.RootElement.EnumerateArray())
                {
                    if (el.ValueKind != JsonValueKind.Object) continue;
                    if (!el.TryGetProperty("field_id", out var fidEl) || !TryGetLong(fidEl, out var fid)) continue;

                    var value = el.TryGetProperty("value", out var v)
                        ? (v.ValueKind == JsonValueKind.String ? v.GetString()
                           : v.ValueKind == JsonValueKind.Number ? v.GetRawText() : null)
                        : null;
                    if (string.IsNullOrWhiteSpace(value)) continue;

                    if (!agg.TryGetValue(fid, out var a))
                    {
                        a = new FieldAgg
                        {
                            Name = el.TryGetProperty("field_name", out var fn) && fn.ValueKind == JsonValueKind.String
                                ? fn.GetString() ?? $"Campo {fid}" : $"Campo {fid}",
                            Code = el.TryGetProperty("field_code", out var fc) && fc.ValueKind == JsonValueKind.String
                                ? fc.GetString() : null,
                            Type = el.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String
                                ? t.GetString() ?? "text" : "text",
                        };
                        agg[fid] = a;
                    }
                    a.Filled++;
                    var key = value.Trim();
                    a.Values[key] = a.Values.GetValueOrDefault(key, 0) + 1;
                }
            }
            catch (JsonException) { /* ignora json malformado */ }
        }

        var fields = agg
            .Select(kv => new DTOs.Kpi.CustomFieldSummaryDto
            {
                FieldId = kv.Key,
                FieldName = kv.Value.Name,
                FieldCode = kv.Value.Code,
                Type = kv.Value.Type,
                Filled = kv.Value.Filled,
                DistinctValues = kv.Value.Values.Count,
                TopValues = kv.Value.Values
                    .OrderByDescending(x => x.Value)
                    .Take(topValues)
                    .Select(x => new DTOs.Kpi.CustomFieldValueCountDto { Value = x.Key, Count = x.Value })
                    .ToList(),
            })
            .OrderByDescending(f => f.Filled)
            .ToList();

        return (total, fields, truncated);
    }

    /// <summary>
    /// Distribuição dos valores de UM campo customizado entre os leads do período
    /// (ex.: campo "Origem" → Instagram 42, Facebook 25, Org 12…). Devolve o top N por
    /// contagem; o restante vira o bucket "Outros". Base do KPI custom tipo gráfico.
    /// </summary>
    public async Task<List<DTOs.Response.KpiBreakdownItemDto>> ComputeBreakdownAsync(
        int clinicId, int? unitId, JsonElement config, DateTime from, DateTime to,
        int topN = 12, CancellationToken ct = default)
    {
        const int MaxScan = 8000;

        var p = ParseConfig(config);
        if (p.FieldId is null && string.IsNullOrWhiteSpace(p.FieldCode))
            return new();

        var q = _db.Leads.AsNoTracking()
            .Where(l => l.TenantId == clinicId && l.CreatedAt >= from && l.CreatedAt <= to
                        && l.CustomFieldsJson != null);
        if (unitId.HasValue)
            q = q.Where(l => l.UnitId == unitId.Value);

        var jsons = await q.OrderByDescending(l => l.CreatedAt)
            .Take(MaxScan).Select(l => l.CustomFieldsJson!).ToListAsync(ct);

        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var json in jsons)
        {
            var v = ExtractFieldValue(json, p.FieldId, p.FieldCode);
            if (string.IsNullOrWhiteSpace(v)) continue;
            var key = v.Trim();
            counts[key] = counts.GetValueOrDefault(key, 0) + 1;
        }

        var ordered = counts.OrderByDescending(x => x.Value).ToList();
        var top = ordered.Take(topN)
            .Select(x => new DTOs.Response.KpiBreakdownItemDto { Label = x.Key, Value = x.Value })
            .ToList();
        var rest = ordered.Skip(topN).Sum(x => x.Value);
        if (rest > 0)
            top.Add(new DTOs.Response.KpiBreakdownItemDto { Label = "Outros", Value = rest });

        return top;
    }

    private sealed class FieldAgg
    {
        public string Name = "";
        public string? Code;
        public string Type = "text";
        public int Filled;
        public Dictionary<string, int> Values = new();
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
