using System.Globalization;
using System.Text.Json;
using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.Models;
using LeadAnalytics.Api.Service.Stages;
using Microsoft.EntityFrameworkCore;

namespace LeadAnalytics.Api.Service;

/// <summary>
/// Persiste eventos normalizados da Kommo (<see cref="LeadEvent"/>) como <see cref="Lead"/>,
/// já isolados por unidade/tenant (<see cref="Unit.ClinicId"/>).
///
/// Idempotente: a chave (ExternalId, TenantId) tem índice único, então reprocessar o
/// mesmo webhook atualiza o lead em vez de duplicar.
///
/// Para cada lead, além de criar/atualizar:
///  • vincula atribuição de origem (Meta CTWA) via <see cref="LeadAttributionService"/>;
///  • registra histórico de etapa (<see cref="LeadStageHistory"/>);
///  • se a unidade tiver mapa <see cref="Unit.KommoStageMapJson"/>, traduz o status_id da
///    Kommo para etapa canônica e dispara a automação de Consulta/Tratamento
///    (<see cref="KommoStageProcessor"/>).
///
/// Entidades que não são "lead" (contact, task, note, talk, unsorted) são só logadas.
/// </summary>
public class KommoIngestionService(
    AppDbContext db,
    KommoStageProcessor stageProcessor,
    ILogger<KommoIngestionService> logger)
{
    private readonly AppDbContext _db = db;
    private readonly KommoStageProcessor _stageProcessor = stageProcessor;
    private readonly ILogger<KommoIngestionService> _logger = logger;

    public async Task<int> IngestAsync(IReadOnlyList<LeadEvent> events, Unit unit, CancellationToken ct = default)
    {
        var stageMap = ParseStageMap(unit.KommoStageMapJson);
        var changed = 0;

        foreach (var ev in events)
        {
            if (!string.Equals(ev.EntityType, "lead", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation(
                    "↪️ Evento ignorado (entidade={Entity} ação={Action} extId={ExtId}) unidade={Slug}",
                    ev.EntityType, ev.Action, ev.ExternalId, unit.Slug);
                continue;
            }

            if (!int.TryParse(ev.ExternalId, out var externalId) || externalId == 0)
            {
                _logger.LogWarning(
                    "⚠️ Lead sem ExternalId numérico válido ('{Id}', ação={Action}) — ignorado",
                    ev.ExternalId, ev.Action);
                continue;
            }

            var lead = await _db.Leads
                .Include(l => l.StageHistory)
                .FirstOrDefaultAsync(l => l.ExternalId == externalId && l.TenantId == unit.ClinicId, ct);

            var action = ev.Action?.ToLowerInvariant();
            var now = DateTime.UtcNow;

            if (action == "delete")
            {
                if (lead is not null)
                {
                    lead.Status = "deleted";
                    lead.UpdatedAt = now;
                    changed++;
                }
                continue;
            }

            // Data REAL da criação na Kommo, se o adaptador/sync souber. Senão, agora.
            var realCreatedAt = ev.KommoCreatedAtUtc.HasValue
                ? DateTime.SpecifyKind(ev.KommoCreatedAtUtc.Value, DateTimeKind.Utc)
                : now;

            if (lead is null)
            {
                lead = new Lead
                {
                    ExternalId = externalId,
                    TenantId = unit.ClinicId,
                    UnitId = unit.Id,
                    Name = string.IsNullOrWhiteSpace(ev.Name) ? "Lead sem nome" : ev.Name!.Trim(),
                    Phone = ev.Phone ?? string.Empty,
                    Email = ev.Email,
                    Source = "Kommo",
                    Status = "new",
                    CreatedAt = realCreatedAt,
                    UpdatedAt = now,
                    LastUpdatedAt = now,
                };
                _db.Leads.Add(lead);
                await _db.SaveChangesAsync(ct); // precisa de Id pra histórico/atribuição
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(ev.Name)) lead.Name = ev.Name!.Trim();
                if (!string.IsNullOrWhiteSpace(ev.Phone)) lead.Phone = ev.Phone!;
                if (!string.IsNullOrWhiteSpace(ev.Email)) lead.Email = ev.Email;
                lead.UnitId ??= unit.Id;
                lead.UpdatedAt = now;
                // BACKFILL: leads antigos foram gravados com CreatedAt='data do 1º sync'.
                // Quando o sync trouxer a data real da Kommo, corrigir aqui.
                if (ev.KommoCreatedAtUtc.HasValue
                    && Math.Abs((lead.CreatedAt - realCreatedAt).TotalMinutes) > 5)
                {
                    lead.CreatedAt = realCreatedAt;
                }
            }

            // Snapshot dos custom_fields da Kommo. Tanto o sync REST quanto o webhook
            // ao vivo preenchem agora. Mesclamos por field_id (em vez de sobrescrever)
            // porque o webhook manda payload PARCIAL — um evento de mudança de etapa não
            // pode apagar campos já capturados (ex.: "Usuário responsável").
            if (ev.CustomFieldsJson != null)
                lead.CustomFieldsJson = MergeCustomFieldsJson(lead.CustomFieldsJson, ev.CustomFieldsJson);
            if (ev.TagsJson != null) lead.TagsJson = ev.TagsJson;

            // Valor do negócio (price da Kommo) — string crua → decimal (invariant).
            if (!string.IsNullOrWhiteSpace(ev.Price)
                && decimal.TryParse(ev.Price, NumberStyles.Any, CultureInfo.InvariantCulture, out var price))
            {
                lead.Price = price;
            }

            // Etapa (status_id da Kommo): se a unidade mapeou esse status_id para uma etapa
            // canônica, gravamos o código LeadStages.* equivalente em CurrentStage — assim
            // as queries do dashboard (que comparam contra LeadStages.*) funcionam para leads
            // vindos da Kommo. CurrentStageId mantém o status_id numérico cru para referência.
            var rawStage = ev.Stage?.Trim();
            int? rawStageId = int.TryParse(rawStage, out var parsedStageId) ? parsedStageId : null;
            var stageChanged = !string.IsNullOrWhiteSpace(rawStage) && rawStageId != lead.CurrentStageId;

            string? canonical = null;
            if (!string.IsNullOrWhiteSpace(rawStage)
                && stageMap.TryGetValue(rawStage, out var canonicalRaw)
                && CanonicalStages.IsKnown(canonicalRaw))
            {
                canonical = canonicalRaw;
            }

            if (stageChanged)
            {
                var mappedLeadStage = canonical != null ? CanonicalStages.ToLeadStage(canonical) : null;
                var newCurrentStage = mappedLeadStage ?? rawStage!;

                lead.CurrentStage = newCurrentStage;
                if (rawStageId.HasValue) lead.CurrentStageId = rawStageId;

                lead.StageHistory.Add(new LeadStageHistory
                {
                    LeadId = lead.Id,
                    StageId = lead.CurrentStageId ?? 0,
                    StageLabel = newCurrentStage,
                    ChangedAt = now,
                });
            }

            // Automação de Consulta/Tratamento — só se a unidade mapeou esse status_id.
            if (canonical != null)
            {
                await _stageProcessor.ApplyAsync(lead, canonical, now, ct);
            }

            changed++;
        }

        if (changed > 0)
            await _db.SaveChangesAsync(ct);

        return changed;
    }

    /// <summary>Lê o mapa status_id→etapa canônica da unidade. Vazio se ausente/ inválido.</summary>
    private Dictionary<string, string> ParseStageMap(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new();
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "KommoStageMapJson inválido — ignorando mapa de etapas");
            return new();
        }
    }

    /// <summary>
    /// Mescla dois arrays de custom fields (shape <c>[{field_id, field_name, …, value}]</c>)
    /// indexando por <c>field_id</c>: os campos de <paramref name="incoming"/> sobrescrevem/
    /// adicionam, e os de <paramref name="existing"/> ausentes no incoming são preservados.
    /// Necessário porque o webhook manda payload parcial. Fallback: se algum lado não for
    /// um array válido, retorna o incoming cru.
    /// </summary>
    private static string MergeCustomFieldsJson(string? existing, string incoming)
    {
        static List<Dictionary<string, JsonElement>> Parse(string? json)
        {
            var list = new List<Dictionary<string, JsonElement>>();
            if (string.IsNullOrWhiteSpace(json)) return list;
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind != JsonValueKind.Array) return list;
                foreach (var el in doc.RootElement.EnumerateArray())
                {
                    if (el.ValueKind != JsonValueKind.Object) continue;
                    var d = new Dictionary<string, JsonElement>();
                    foreach (var p in el.EnumerateObject()) d[p.Name] = p.Value.Clone();
                    list.Add(d);
                }
            }
            catch (JsonException) { /* json malformado — ignora */ }
            return list;
        }

        static string? KeyOf(Dictionary<string, JsonElement> d)
            => d.TryGetValue("field_id", out var fid) && fid.ValueKind == JsonValueKind.Number
                ? fid.GetRawText()
                : (d.TryGetValue("field_name", out var fn) && fn.ValueKind == JsonValueKind.String
                    ? "name:" + fn.GetString()
                    : null);

        var merged = Parse(existing);
        var incomingList = Parse(incoming);
        if (incomingList.Count == 0) return existing ?? incoming;

        var indexByKey = new Dictionary<string, int>();
        for (var i = 0; i < merged.Count; i++)
        {
            var k = KeyOf(merged[i]);
            if (k != null) indexByKey[k] = i;
        }

        foreach (var item in incomingList)
        {
            var k = KeyOf(item);
            if (k != null && indexByKey.TryGetValue(k, out var idx))
                merged[idx] = item;       // sobrescreve o campo existente
            else
            {
                if (k != null) indexByKey[k] = merged.Count;
                merged.Add(item);          // campo novo
            }
        }

        return JsonSerializer.Serialize(merged);
    }
}
