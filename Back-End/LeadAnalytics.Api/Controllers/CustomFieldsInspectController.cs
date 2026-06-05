using System.Text.Json;
using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.Service;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LeadAnalytics.Api.Controllers;

/// <summary>
/// Endpoint de DEBUG pra diagnosticar por que os campos customizados não
/// aparecem no dashboard. Devolve tudo cru:
///
///   GET /api/admin/custom-fields/inspect?unitId=12
///
/// Mostra: config Kommo da unit (com/sem token), contagem de leads,
/// distribuição (quantos têm json, quantos têm value preenchido) e 3 amostras
/// do JSON cru pra batermos olho.
///
/// Pode ficar em prod — é só leitura, autenticado por tenant.
/// </summary>
[ApiController]
// ⚠️ TEMPORÁRIO: [AllowAnonymous] só pra debug ao vivo do bug dos campos
// customizados. REVERTER pra [Authorize] depois que terminar o diagnóstico.
[AllowAnonymous]
[Route("api/admin/custom-fields")]
public class CustomFieldsInspectController(
    AppDbContext db,
    TenantUnitGuard tenantGuard,
    KommoApiClient kommoApi) : ControllerBase
{
    /// <summary>
    /// Compara o JSON salvo no NOSSO banco com o que a Kommo devolve AO VIVO
    /// pra UM lead específico. Resolve a pergunta:
    ///   "A Kommo está mandando os campos preenchidos pelas SDRs ou não?"
    /// </summary>
    [HttpGet("compare/{externalId:long}")]
    public async Task<IActionResult> CompareWithKommo(
        long externalId,
        [FromQuery] int unitId,
        CancellationToken ct = default)
    {
        var unit = await db.Units.AsNoTracking().FirstOrDefaultAsync(u => u.Id == unitId, ct);
        if (unit is null) return NotFound(new { error = "unit não encontrada" });
        if (string.IsNullOrWhiteSpace(unit.KommoSubdomain) || string.IsNullOrWhiteSpace(unit.KommoAccessToken))
            return BadRequest(new { error = "unit sem Kommo configurado" });

        // Lê do nosso banco
        var ourLead = await db.Leads
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.TenantId == unit.ClinicId && l.ExternalId == (int)externalId, ct);

        // Bate na Kommo ao vivo
        object? kommoRaw = null;
        string? kommoError = null;
        try
        {
            var live = await kommoApi.GetLeadByIdAsync(unit.KommoSubdomain!, unit.KommoAccessToken!, externalId, ct);
            kommoRaw = live is null
                ? new { note = "Kommo retornou null/404" }
                : new
                {
                    id = live.Id,
                    name = live.Name,
                    updated_at = live.UpdatedAt,
                    custom_fields_values_count = live.CustomFieldsValues?.Count ?? 0,
                    custom_fields_values = live.CustomFieldsValues?.Select(f => new
                    {
                        field_id = f.FieldId,
                        field_name = f.FieldName,
                        field_code = f.FieldCode,
                        field_type = f.FieldType,
                        values_count = f.Values?.Count ?? 0,
                        values = f.Values?.Select(v => new
                        {
                            value_raw = v.Value?.GetRawText(),
                            value_string = v.GetStringValue(),
                            enum_id = v.EnumId,
                            enum_code = v.EnumCode,
                        }),
                    }),
                };
        }
        catch (Exception ex)
        {
            kommoError = ex.Message;
        }

        return Ok(new
        {
            externalId,
            unit = new { unit.Id, unit.Name, unit.KommoSubdomain },
            ourDb = ourLead is null
                ? null
                : (object)new
                {
                    id = ourLead.Id,
                    externalId = ourLead.ExternalId,
                    name = ourLead.Name,
                    updatedAt = ourLead.UpdatedAt,
                    createdAt = ourLead.CreatedAt,
                    customFieldsJson = ourLead.CustomFieldsJson,
                    customFieldsJsonLength = ourLead.CustomFieldsJson?.Length ?? 0,
                },
            kommoLive = kommoRaw,
            kommoError,
            note = "Se kommoLive.custom_fields_values_count > 1 e ourDb.customFieldsJson tem 1 campo → bug no sync. Se ambos têm 1, é a Kommo mesmo.",
        });
    }

    [HttpGet("inspect")]
    public async Task<IActionResult> Inspect(
        [FromQuery] int? unitId,
        [FromQuery] int sampleSize = 3,
        CancellationToken ct = default)
    {
        // ⚠️ TEMPORÁRIO: bypass do tenantGuard. Aceita unitId direto da
        // querystring pra debug. REVERTER junto com [Authorize] acima.
        int? tenantId = null;
        if (unitId.HasValue)
        {
            var unitClinic = await db.Units.AsNoTracking()
                .Where(u => u.Id == unitId.Value)
                .Select(u => (int?)u.ClinicId)
                .FirstOrDefaultAsync(ct);
            tenantId = unitClinic;
        }

        // Unit info — confirma se está configurada pra Kommo
        var unit = unitId.HasValue
            ? await db.Units.AsNoTracking().FirstOrDefaultAsync(u => u.Id == unitId.Value, ct)
            : null;

        var unitInfo = unit is null ? null : new
        {
            id = unit.Id,
            name = unit.Name,
            isActive = unit.IsActive,
            kommoSubdomain = unit.KommoSubdomain,
            hasKommoToken = !string.IsNullOrWhiteSpace(unit.KommoAccessToken),
            tokenLength = unit.KommoAccessToken?.Length ?? 0,
        };

        // Contagens — total de leads vs. quantos têm CustomFieldsJson
        var baseQ = db.Leads.AsNoTracking().Where(l => l.TenantId == (tenantId ?? l.TenantId));
        if (unitId.HasValue) baseQ = baseQ.Where(l => l.UnitId == unitId.Value);

        var totalLeads = await baseQ.CountAsync(ct);
        var leadsWithJson = await baseQ.CountAsync(l => l.CustomFieldsJson != null, ct);

        // Última atualização e último sync (proxy: UpdatedAt mais recente)
        var lastUpdated = await baseQ
            .OrderByDescending(l => l.UpdatedAt)
            .Select(l => (DateTime?)l.UpdatedAt)
            .FirstOrDefaultAsync(ct);

        // Amostras — 3 leads mais recentemente atualizados com JSON
        var samples = await baseQ
            .Where(l => l.CustomFieldsJson != null)
            .OrderByDescending(l => l.UpdatedAt)
            .Take(Math.Clamp(sampleSize, 1, 10))
            .Select(l => new
            {
                id = l.Id,
                externalId = l.ExternalId,
                name = l.Name,
                updatedAt = l.UpdatedAt,
                createdAt = l.CreatedAt,
                customFieldsJsonRaw = l.CustomFieldsJson,
            })
            .ToListAsync(ct);

        // Para cada amostra: parse + estatística por campo
        var sampleDetails = samples.Select(s =>
        {
            var parsed = TryParseFields(s.customFieldsJsonRaw);
            return new
            {
                s.id,
                s.externalId,
                s.name,
                s.updatedAt,
                s.createdAt,
                totalFieldsInJson = parsed?.Count ?? 0,
                fieldsWithValue = parsed?.Count(f => !string.IsNullOrWhiteSpace(f.Value)) ?? 0,
                fieldsWithNullValue = parsed?.Count(f => string.IsNullOrWhiteSpace(f.Value)) ?? 0,
                fields = parsed?.Select(f => new
                {
                    f.FieldId,
                    f.FieldName,
                    f.Type,
                    f.Value,
                    f.EnumId,
                    f.EnumCode,
                    valueIsNullOrEmpty = string.IsNullOrWhiteSpace(f.Value),
                }).ToList(),
                rawJson = s.customFieldsJsonRaw,
            };
        }).ToList();

        // Agregado global: quantos leads têm cada campo PREENCHIDO (value não-vazio)
        // Cap em 1000 leads pra não explodir memória
        var allJsons = await baseQ
            .Where(l => l.CustomFieldsJson != null)
            .OrderByDescending(l => l.UpdatedAt)
            .Take(1000)
            .Select(l => l.CustomFieldsJson!)
            .ToListAsync(ct);

        var perField = new Dictionary<long, (string Name, int Filled, int NullValue)>();
        foreach (var json in allJsons)
        {
            var parsed = TryParseFields(json);
            if (parsed is null) continue;
            foreach (var f in parsed)
            {
                if (!perField.TryGetValue(f.FieldId, out var row))
                    row = (f.FieldName ?? $"Campo {f.FieldId}", 0, 0);

                if (!string.IsNullOrWhiteSpace(f.Value))
                    row.Filled++;
                else
                    row.NullValue++;
                perField[f.FieldId] = row;
            }
        }

        return Ok(new
        {
            unit = unitInfo,
            counts = new
            {
                totalLeads,
                leadsWithJson,
                leadsWithoutJson = totalLeads - leadsWithJson,
                lastUpdatedAt = lastUpdated,
            },
            globalFieldStats = perField
                .OrderByDescending(kv => kv.Value.Filled)
                .Select(kv => new
                {
                    fieldId = kv.Key,
                    fieldName = kv.Value.Name,
                    leadsWithValue = kv.Value.Filled,
                    leadsWithNullValue = kv.Value.NullValue,
                    totalAppearances = kv.Value.Filled + kv.Value.NullValue,
                })
                .ToList(),
            samples = sampleDetails,
            scanLimit = 1000,
            note = "Se 'leadsWithJson' é baixo: sync não rodou. Se 'fieldsWithNullValue' alto: select sem texto.",
        });
    }

    private static List<ParsedField>? TryParseFields(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;

            var list = new List<ParsedField>();
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                if (el.ValueKind != JsonValueKind.Object) continue;

                long fid = 0;
                if (el.TryGetProperty("field_id", out var fidEl))
                {
                    if (fidEl.ValueKind == JsonValueKind.Number) fid = fidEl.GetInt64();
                    else if (fidEl.ValueKind == JsonValueKind.String && long.TryParse(fidEl.GetString(), out var parsed)) fid = parsed;
                }

                string? value = null;
                if (el.TryGetProperty("value", out var v) && v.ValueKind != JsonValueKind.Null)
                {
                    value = v.ValueKind switch
                    {
                        JsonValueKind.String => v.GetString(),
                        JsonValueKind.Number => v.GetRawText(),
                        JsonValueKind.True => "true",
                        JsonValueKind.False => "false",
                        _ => v.ToString(),
                    };
                }

                list.Add(new ParsedField
                {
                    FieldId = fid,
                    FieldName = el.TryGetProperty("field_name", out var fn) && fn.ValueKind == JsonValueKind.String ? fn.GetString() : null,
                    Type = el.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null,
                    Value = value,
                    EnumId = el.TryGetProperty("enum_id", out var ei) && ei.ValueKind == JsonValueKind.Number ? ei.GetInt64() : (long?)null,
                    EnumCode = el.TryGetProperty("enum_code", out var ec) && ec.ValueKind == JsonValueKind.String ? ec.GetString() : null,
                });
            }
            return list;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private class ParsedField
    {
        public long FieldId { get; set; }
        public string? FieldName { get; set; }
        public string? Type { get; set; }
        public string? Value { get; set; }
        public long? EnumId { get; set; }
        public string? EnumCode { get; set; }
    }
}
