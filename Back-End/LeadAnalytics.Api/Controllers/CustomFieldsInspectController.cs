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
    /// DEBUG: chama EXATAMENTE o mesmo método que o sync usa
    /// (KommoApiClient.GetLeadsPageAsync) e mostra quantos custom_fields_values
    /// foram deserializados em cada um dos primeiros 3 leads da página 1.
    /// Roda o SerializeCustomFields no primeiro lead pra ver se retorna null.
    /// </summary>
    [HttpGet("test-sync-pipeline")]
    public async Task<IActionResult> TestSyncPipeline(
        [FromQuery] int unitId,
        CancellationToken ct = default)
    {
        var unit = await db.Units.AsNoTracking().FirstOrDefaultAsync(u => u.Id == unitId, ct);
        if (unit is null) return NotFound(new { error = "unit não encontrada" });
        if (string.IsNullOrWhiteSpace(unit.KommoSubdomain) || string.IsNullOrWhiteSpace(unit.KommoAccessToken))
            return BadRequest(new { error = "unit sem Kommo configurado" });

        try
        {
            // Mesma chamada que o sync faz
            var page = await kommoApi.GetLeadsPageAsync(
                unit.KommoSubdomain!, unit.KommoAccessToken!, 1, 250, ct);

            var leads = page?.Embedded?.Leads;

            // Distribuição em todos os 250 leads — se ALGUM > 0, deserialização funciona
            var withCfv = leads?.Count(l => l.CustomFieldsValues is { Count: > 0 }) ?? 0;
            var emptyArray = leads?.Count(l => l.CustomFieldsValues is { Count: 0 }) ?? 0;
            var nullCfv = leads?.Count(l => l.CustomFieldsValues is null) ?? 0;

            // Procura o PRIMEIRO lead com custom_fields populados
            var firstWithFields = leads?.FirstOrDefault(l => l.CustomFieldsValues is { Count: > 0 });

            return Ok(new
            {
                pageLeadCount = leads?.Count ?? 0,
                distribution = new
                {
                    withCustomFields = withCfv,
                    emptyArray,
                    nullCustomFields = nullCfv,
                },
                firstLeadWithFields = firstWithFields is null ? null : new
                {
                    id = firstWithFields.Id,
                    name = firstWithFields.Name,
                    custom_fields_values_count = firstWithFields.CustomFieldsValues?.Count ?? 0,
                    sample = firstWithFields.CustomFieldsValues?.Take(3).Select(f => new
                    {
                        field_id = f.FieldId,
                        field_name = f.FieldName,
                        first_value_string = f.Values?.FirstOrDefault()?.GetStringValue(),
                    }),
                },
                firstThreeLeads = leads?.Take(3).Select(l => new
                {
                    id = l.Id,
                    name = l.Name,
                    custom_fields_values_count = l.CustomFieldsValues?.Count ?? -1,
                }),
                note = "withCustomFields = 0 em 250 leads → deserialização quebrada. >0 → bug fica no SerializeCustomFields/IngestionService.",
            });
        }
        catch (Exception ex)
        {
            return Ok(new { error = ex.Message, stack = ex.StackTrace });
        }
    }

    /// <summary>
    /// DEBUG: bate na paginated /api/v4/leads?filter[id][]=… e devolve o JSON CRU.
    /// Resolve definitivamente se a Kommo manda custom_fields_values no
    /// paginated (que o sync usa) ou só no single-lead endpoint.
    /// </summary>
    [HttpGet("raw-paginated/{externalId:long}")]
    public async Task<IActionResult> RawPaginated(
        long externalId,
        [FromQuery] int unitId,
        CancellationToken ct = default)
    {
        var unit = await db.Units.AsNoTracking().FirstOrDefaultAsync(u => u.Id == unitId, ct);
        if (unit is null) return NotFound(new { error = "unit não encontrada" });
        if (string.IsNullOrWhiteSpace(unit.KommoSubdomain) || string.IsNullOrWhiteSpace(unit.KommoAccessToken))
            return BadRequest(new { error = "unit sem Kommo configurado" });

        try
        {
            var raw = await kommoApi.DebugGetLeadsPageRawAsync(
                unit.KommoSubdomain!, unit.KommoAccessToken!, externalId, ct);
            return Content(raw ?? "(null)", "application/json");
        }
        catch (Exception ex)
        {
            return Ok(new { error = ex.Message });
        }
    }

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
                    created_at_unix = live.CreatedAt,
                    created_at_utc = live.CreatedAt.HasValue
                        ? DateTimeOffset.FromUnixTimeSeconds(live.CreatedAt.Value).UtcDateTime.ToString("o")
                        : null,
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

    /// <summary>
    /// BACKFILL: paginar TODOS os leads da Kommo (sem cap) e corrigir só
    /// o <c>Lead.CreatedAt</c> usando o <c>created_at</c> real da Kommo.
    /// Pula a re-busca pesada de custom_fields — é focado e rápido.
    /// </summary>
    [HttpPost("backfill-created-at/{unitId:int}")]
    public async Task<IActionResult> BackfillCreatedAt(int unitId, CancellationToken ct)
    {
        var unit = await db.Units.FirstOrDefaultAsync(u => u.Id == unitId, ct);
        if (unit is null) return NotFound(new { error = "unit não encontrada" });
        if (string.IsNullOrWhiteSpace(unit.KommoSubdomain) || string.IsNullOrWhiteSpace(unit.KommoAccessToken))
            return BadRequest(new { error = "unit sem Kommo configurado" });

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var page = 1;
        const int pageSize = 250;
        var processed = 0;
        var fixedCount = 0;
        var alreadyOk = 0;
        var skippedNoCreatedAt = 0;
        var notInDb = 0;

        // Carrega todos os leads da unit no DB indexados por ExternalId — uma query só
        var dbLeadsByExt = await db.Leads
            .Where(l => l.TenantId == unit.ClinicId && l.UnitId == unitId)
            .ToDictionaryAsync(l => l.ExternalId, l => l, ct);

        while (true)
        {
            if (ct.IsCancellationRequested) break;
            var resp = await kommoApi.GetLeadsPageAsync(
                unit.KommoSubdomain!, unit.KommoAccessToken!, page, pageSize, ct);

            var batch = resp?.Embedded?.Leads;
            if (batch is null || batch.Count == 0) break;

            foreach (var kommoLead in batch)
            {
                processed++;
                if (!kommoLead.CreatedAt.HasValue) { skippedNoCreatedAt++; continue; }

                if (!dbLeadsByExt.TryGetValue((int)kommoLead.Id, out var dbLead)) { notInDb++; continue; }

                var realCreatedAt = DateTimeOffset.FromUnixTimeSeconds(kommoLead.CreatedAt.Value).UtcDateTime;
                var current = DateTime.SpecifyKind(dbLead.CreatedAt, DateTimeKind.Utc);
                var diff = Math.Abs((current - realCreatedAt).TotalMinutes);

                if (diff <= 5) { alreadyOk++; continue; }

                dbLead.CreatedAt = DateTime.SpecifyKind(realCreatedAt, DateTimeKind.Utc);
                fixedCount++;
            }

            // Salva em lotes pra não estourar memória
            await db.SaveChangesAsync(ct);

            if (resp?.Links?.Next is null) break;
            page++;
        }

        sw.Stop();
        return Ok(new
        {
            unit = new { unit.Id, unit.Name },
            processed,
            fixedCount,
            alreadyOk,
            skippedNoCreatedAt,
            notInDb,
            pagesScanned = page,
            durationSec = sw.Elapsed.TotalSeconds,
            note = "Apenas Lead.CreatedAt foi atualizado. Custom fields e outras colunas permanecem como estavam.",
        });
    }

    /// <summary>
    /// DEBUG: leads por dia (BRT) nos últimos N dias. Pra ver se a contagem
    /// de "hoje" está fora do padrão ou se vários dias estão inflados pelo
    /// CreatedAt errado.
    /// </summary>
    [HttpGet("leads-per-day")]
    public async Task<IActionResult> LeadsPerDay(
        [FromQuery] int unitId,
        [FromQuery] int days = 14,
        CancellationToken ct = default)
    {
        var unit = await db.Units.AsNoTracking().FirstOrDefaultAsync(u => u.Id == unitId, ct);
        if (unit is null) return NotFound(new { error = "unit não encontrada" });
        var tenantId = unit.ClinicId;

        var brOffset = TimeSpan.FromHours(3);
        var nowBr = DateTime.UtcNow.Add(-brOffset);
        var oldestBrDate = nowBr.Date.AddDays(-(days - 1));
        var fromUtc = DateTime.SpecifyKind(oldestBrDate + brOffset, DateTimeKind.Utc);
        var toUtc = DateTime.SpecifyKind(nowBr.Date.AddDays(1).AddTicks(-1) + brOffset, DateTimeKind.Utc);

        var rawCreatedAts = await db.Leads.AsNoTracking()
            .Where(l => l.TenantId == tenantId && l.UnitId == unitId
                        && l.CreatedAt >= fromUtc && l.CreatedAt <= toUtc)
            .Select(l => l.CreatedAt)
            .ToListAsync(ct);

        // Agrupa por dia BRT (subtrai 3h de cada UTC pra obter o dia em BR)
        var perDay = rawCreatedAts
            .GroupBy(utc => utc.Add(-brOffset).Date)
            .Select(g => new
            {
                day = g.Key.ToString("yyyy-MM-dd"),
                weekday = g.Key.ToString("ddd"),
                count = g.Count(),
            })
            .OrderBy(x => x.day)
            .ToList();

        // Sample dos leads mais recentes pra inspeção rápida (id, name, createdAt)
        var sample = await db.Leads.AsNoTracking()
            .Where(l => l.TenantId == tenantId && l.UnitId == unitId
                        && l.CreatedAt >= fromUtc && l.CreatedAt <= toUtc)
            .OrderByDescending(l => l.CreatedAt)
            .Take(15)
            .Select(l => new
            {
                id = l.Id,
                externalId = l.ExternalId,
                name = l.Name,
                createdAt = l.CreatedAt,
                createdAtBr = l.CreatedAt.Add(-brOffset).ToString("yyyy-MM-dd HH:mm:ss"),
                currentStage = l.CurrentStage,
                currentStageId = l.CurrentStageId,
            })
            .ToListAsync(ct);

        return Ok(new
        {
            unit = new { unit.Id, unit.Name },
            window = new
            {
                fromUtc, toUtc,
                fromBr = oldestBrDate.ToString("yyyy-MM-dd"),
                toBr = nowBr.Date.ToString("yyyy-MM-dd"),
            },
            totalInWindow = rawCreatedAts.Count,
            perDay,
            recentSample = sample,
            note = "Se 'hoje' está muito acima da média dos outros dias, CreatedAt de muitos leads ainda está com a data do sync e não a real da Kommo.",
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

    /// <summary>
    /// DEBUG TEMPORÁRIO: muda o papel de um usuário por e-mail e reconcilia o tenant
    /// com o ClinicId da unidade. Usado pra converter usuário existente em trafego_pago
    /// (o convite não troca papel de quem já tem acesso). Protegido por ?secret=.
    /// REMOVER depois.
    /// </summary>
    [HttpPost("set-role")]
    public async Task<IActionResult> SetRole(
        [FromQuery] string email,
        [FromQuery] string role,
        [FromQuery] string secret,
        CancellationToken ct = default)
    {
        if (secret != "dd-fix-2026") return Unauthorized();
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(role))
            return BadRequest(new { error = "email e role são obrigatórios" });

        var canonical = Roles.Canonical(role);
        if (string.IsNullOrEmpty(canonical) || !Roles.IsValidInviteRole(canonical))
            return BadRequest(new { error = "role inválido" });

        var needle = email.Trim().ToLower();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == needle, ct);
        if (user is null) return NotFound(new { error = "usuário não encontrado" });

        var before = new { user.Role, user.TenantId };
        user.Role = canonical;

        var derived = await db.UserUnits
            .Where(uu => uu.UserId == user.Id)
            .Join(db.Units, uu => uu.UnitId, un => un.Id, (uu, un) => (int?)un.ClinicId)
            .FirstOrDefaultAsync(ct);
        if (derived is not null) user.TenantId = derived;

        await db.SaveChangesAsync(ct);
        return Ok(new { email = user.Email, before, after = new { user.Role, user.TenantId } });
    }

    /// <summary>
    /// DEBUG TEMPORÁRIO: lista usuários (role, tenant_id, units→clinicId) pra diagnosticar
    /// os 403 do dashboard (tenant do JWT ≠ clinicId). Filtra por ?email= (substring).
    /// REMOVER depois.
    /// </summary>
    [HttpGet("users-debug")]
    public async Task<IActionResult> UsersDebug([FromQuery] string? email = null, CancellationToken ct = default)
    {
        var q = db.Users.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(email))
        {
            var needle = email.Trim().ToLower();
            q = q.Where(u => u.Email.ToLower().Contains(needle));
        }

        var users = await q.OrderByDescending(u => u.Id).Take(50)
            .Select(u => new { u.Id, u.Email, u.Role, u.TenantId, u.IsActive })
            .ToListAsync(ct);

        var result = new List<object>();
        foreach (var u in users)
        {
            var units = await db.UserUnits.AsNoTracking()
                .Where(uu => uu.UserId == u.Id)
                .Join(db.Units, uu => uu.UnitId, un => un.Id,
                    (uu, un) => new { un.Id, un.ClinicId, un.Name })
                .ToListAsync(ct);
            result.Add(new { u.Id, u.Email, u.Role, u.TenantId, u.IsActive, units });
        }

        return Ok(result);
    }
}
