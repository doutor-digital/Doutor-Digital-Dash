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
    KpiConfigService kpiConfig,
    ILogger<KommoIngestionService> logger)
{
    private readonly AppDbContext _db = db;
    private readonly KommoStageProcessor _stageProcessor = stageProcessor;
    private readonly KpiConfigService _kpiConfig = kpiConfig;
    private readonly ILogger<KommoIngestionService> _logger = logger;

    /// <param name="recordStageHistory">
    /// Só o webhook AO VIVO (lead:status) sabe o instante real da transição — aí grava
    /// <see cref="LeadStageHistory"/> datado. O sync REST passa <c>false</c>: ele só conhece
    /// <c>updated_at</c> (última modificação qualquer, não a entrada na etapa), então atualiza
    /// <c>CurrentStage</c> mas NÃO crava data de entrada — isso é trabalho do webhook ou do
    /// backfill via API de eventos (<see cref="KommoStageHistoryBackfillService"/>). Era a
    /// causa do "211 agendados no dia": o sync/heal carimbava updated_at como data de entrada.
    /// </param>
    public async Task<int> IngestAsync(IReadOnlyList<LeadEvent> events, Unit unit, CancellationToken ct = default,
        IReadOnlyDictionary<string, string>? stageMapOverride = null,
        bool recordStageHistory = false)
    {
        // Mapa efetivo de etapas: começa pelo mapa derivado dos NOMES das etapas da Kommo
        // (override passado pelo sync — resolve agendados mesmo sem KommoStageMapJson) e
        // sobrepõe o mapa explícito da unidade, que tem prioridade (config do admin vence).
        var stageMap = stageMapOverride is null
            ? new Dictionary<string, string>()
            : new Dictionary<string, string>(stageMapOverride);
        foreach (var kv in ParseStageMap(unit.KommoStageMapJson))
            stageMap[kv.Key] = kv.Value;

        // Mapeamento dos custom fields do Perfil do Lead (escolhido em Configurações).
        // Usado pra popular Lead.AppointmentScheduledAt e Lead.ConsultationValue direto
        // do CustomFieldsJson, sem precisar de edição manual no painel do lead.
        var profileFields = unit.Id > 0
            ? await _kpiConfig.GetLeadProfileConfigAsync(unit.Id, ct)
            : new KpiConfigService.LeadProfileFields();
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

            // Data REAL da mudança na Kommo (last_modified/updated_at). Usada como
            // ChangedAt no LeadStageHistory pra o KPI por dia ("agendados em 09/06")
            // refletir o que aconteceu na Kommo, não quando o backend processou.
            // Fallback: now — se o payload não trouxer (improvável em lead:status/update).
            var stageChangedAt = ev.KommoModifiedAtUtc ?? now;

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

            // Data REAL de criação — fonte da verdade do dashboard quando o CreatedAt do nosso
            // backend é a "data do 1º sync" (leads importados em lote entravam todos no mês do
            // sync e inflavam o período). Precedência:
            //  1º) custom field "Data de criação lead" (preenchido pelo backfill Cloudia/CSV —
            //      traz a data ORIGINAL, ex.: lead de 2025 migrado);
            //  2º) created_at NATIVO da Kommo (ev.KommoCreatedAtUtc) — melhor que o nosso
            //      CreatedAt; só preenche quando ainda está vazio, pra não sobrescrever a data
            //      original vinda do custom field num webhook parcial.
            var originalFromField = TryExtractOriginalCreatedAt(lead.CustomFieldsJson);
            if (originalFromField.HasValue)
                lead.OriginalCreatedAt = originalFromField;
            else if (ev.KommoCreatedAtUtc.HasValue)
                lead.OriginalCreatedAt ??= realCreatedAt;

            // Data do agendamento da consulta (campo escolhido pelo analista em Configurações
            // → Perfil do Lead → "Data de agendamento"). Alimenta o sino de notificação e o
            // card Consultas → Próximos agendamentos. Fallback por nome ("agendamento") quando
            // o id não foi mapeado.
            var apptDate = TryExtractDateFromCustomFields(
                lead.CustomFieldsJson, profileFields.AppointmentFieldId,
                n => n.Contains("agendamento"));
            if (apptDate.HasValue && apptDate.Value != lead.AppointmentScheduledAt)
            {
                // Data MUDOU (incluindo de null → valor) — atualiza e registra QUANDO
                // foi preenchida. Card Consultas conta por essa data de preenchimento
                // (produtividade do dia da SDR), não pela data da consulta em si.
                lead.AppointmentScheduledAt = apptDate;
                lead.AppointmentScheduledAtFilledAt = ev.KommoModifiedAtUtc ?? now;
            }

            // Qualificação do lead (campo select escolhido em Configurações → Perfil do Lead →
            // "Qualificação", ex.: Frio/Morno/Quente). Contamos a qualificação pela data em que
            // a SDR PREENCHE/muda o campo (produtividade do dia), não pela criação do lead.
            // Fallback por nome ("qualifica") quando o id não foi mapeado.
            var qualifValue = ExtractFieldRaw(
                lead.CustomFieldsJson, profileFields.QualificacaoFieldId,
                n => n.Contains("qualifica"))?.Trim();
            if (!string.IsNullOrWhiteSpace(qualifValue) && qualifValue != lead.Qualification)
            {
                // Valor MUDOU (incluindo de null → valor) — atualiza e registra QUANDO foi
                // preenchido. Webhook parcial que não traz o campo lê o valor já mesclado do
                // CustomFieldsJson, então não re-carimba (valor inalterado).
                lead.Qualification = qualifValue;
                lead.QualificationFilledAt = ev.KommoModifiedAtUtc ?? now;
            }

            // Valor da consulta (campo escolhido em Configurações → "Valor da consulta").
            // Alimenta o card Consultas → Valor total. Não sobrescreve um valor já preenchido
            // manualmente na Revisão comercial — só seta quando o SQL está nulo, evitando
            // que um valor zero/em-branco do Kommo apague um valor já corrigido.
            if (!lead.ConsultationValue.HasValue)
            {
                var consVal = TryExtractDecimalFromCustomFields(
                    lead.CustomFieldsJson, profileFields.ValorConsultaFieldId,
                    n => n.Contains("valor") && n.Contains("consulta"));
                if (consVal.HasValue) lead.ConsultationValue = consVal;
            }

            // Tentativas de resgate (multi-select "Tentativas de resgastes" — typo da Kommo).
            // Antes só o backfill da API de eventos (24h) criava RecoveryAttempt; agora o
            // webhook ao vivo também grava em tempo real, dedup por (LeadId, Outcome). Usa
            // o updated_at do lead na Kommo como CreatedAt (close enough — KPI agrega por
            // dia). O backfill noturno continua, e a query do KPI aceita ambos os sources.
            await UpsertRecoveryAttemptsAsync(lead, unit.ClinicId, ev.KommoModifiedAtUtc ?? now, ct);

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
                && stageMap.TryGetValue(rawStage, out var canonicalRaw))
            {
                // Resolve é tolerante: aceita o nome canônico exato, o nome da etapa da
                // Kommo com prefixo ("04_AGENDADO_SEM_PAGAMENTO") ou por palavra-chave.
                // Antes exigíamos o canônico exato (IsKnown) — qualquer outra grafia caía
                // no fallback de status_id cru e sumia dos cards (bug do "agendado").
                canonical = CanonicalStages.Resolve(canonicalRaw);
            }

            var mappedLeadStage = canonical != null ? CanonicalStages.ToLeadStage(canonical) : null;

            if (stageChanged)
            {
                var prevStage = lead.CurrentStage; // etapa ANTES da mudança (pra detectar bounce intra-agendado)
                var newCurrentStage = mappedLeadStage ?? rawStage!;

                lead.CurrentStage = newCurrentStage;
                if (rawStageId.HasValue) lead.CurrentStageId = rawStageId;

                // Mover ENTRE "agendado com pagamento" e "agendado sem pagamento" (04↔05) é
                // reclassificação, não um agendamento novo — não conta como entrada. Atualiza a
                // etapa atual, mas não grava linha de histórico datada.
                var intraAgendadoBounce = LeadStages.IsScheduled(newCurrentStage) && LeadStages.IsScheduled(prevStage);

                // Só grava entrada DATADA quando a fonte conhece o instante real da transição
                // (webhook ao vivo). No sync, stageChangedAt = updated_at da Kommo, que não é a
                // data de entrada na etapa — datar aqui inflava o KPI por dia. O backfill da API
                // de eventos repõe o histórico real do passado.
                if (recordStageHistory && !intraAgendadoBounce)
                {
                    lead.StageHistory.Add(new LeadStageHistory
                    {
                        LeadId = lead.Id,
                        StageId = lead.CurrentStageId ?? 0,
                        StageLabel = newCurrentStage,
                        ChangedAt = stageChangedAt,
                        EntrySource = LeadStageHistory.SourceWebhook,
                    });
                }
            }
            else if (mappedLeadStage != null
                && !string.Equals(lead.CurrentStage, mappedLeadStage, StringComparison.Ordinal))
            {
                // Heal retroativo: o status_id não mudou (stageChanged=false), mas o lead
                // ficou gravado com o status_id cru (ex.: "67548620") porque o mapa antes
                // exigia o canônico exato. Corrige o CurrentStage em vigor — mas NÃO crava
                // linha de histórico datada: não sabemos quando o lead entrou na etapa
                // (updated_at não serve). A data real vem do backfill via API de eventos.
                lead.CurrentStage = mappedLeadStage;
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

    /// <summary>
    /// Extrai a data do custom field "Data de criação lead" do CustomFieldsJson.
    /// Casa por nome (contém "data" + "cria") pra funcionar em qualquer unidade,
    /// independente do field_id. Aceita unix em segundos ou milissegundos.
    /// </summary>
    private static DateTime? TryExtractOriginalCreatedAt(string? customFieldsJson)
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
                var name = fn.GetString()?.ToLowerInvariant() ?? "";
                if (!(name.Contains("data") && name.Contains("cria"))) continue;

                if (!el.TryGetProperty("values", out var vals)) continue;
                string? rawValue = null;
                if (vals.ValueKind == JsonValueKind.Array)
                {
                    foreach (var v in vals.EnumerateArray())
                    {
                        if (v.ValueKind == JsonValueKind.Object && v.TryGetProperty("value", out var inner))
                        {
                            rawValue = inner.ValueKind == JsonValueKind.Number ? inner.GetRawText() : inner.GetString();
                            break;
                        }
                        if (v.ValueKind == JsonValueKind.Number || v.ValueKind == JsonValueKind.String)
                        {
                            rawValue = v.ValueKind == JsonValueKind.Number ? v.GetRawText() : v.GetString();
                            break;
                        }
                    }
                }
                else if (vals.ValueKind == JsonValueKind.Number) rawValue = vals.GetRawText();
                else if (vals.ValueKind == JsonValueKind.String) rawValue = vals.GetString();

                if (string.IsNullOrWhiteSpace(rawValue)) continue;
                if (!long.TryParse(rawValue, NumberStyles.Any, CultureInfo.InvariantCulture, out var num)) continue;
                if (num <= 0) continue;
                try
                {
                    return num > 99999999999L
                        ? DateTimeOffset.FromUnixTimeMilliseconds(num).UtcDateTime
                        : DateTimeOffset.FromUnixTimeSeconds(num).UtcDateTime;
                }
                catch { return null; }
            }
        }
        catch (JsonException) { /* ignora json malformado */ }
        return null;
    }

    /// <summary>
    /// Extrai uma data de um custom field do CustomFieldsJson. Prefere matching por
    /// field_id (id mapeado); cai pra match por nome quando o id não está configurado.
    /// Aceita ISO, "yyyy-MM-dd", "dd/MM/yyyy" e unix (s/ms) — formatos comuns da Kommo.
    /// </summary>
    internal static DateTime? TryExtractDateFromCustomFields(
        string? customFieldsJson, long? fieldId, Func<string, bool> nameMatches)
    {
        var raw = ExtractFieldRaw(customFieldsJson, fieldId, nameMatches);
        if (string.IsNullOrWhiteSpace(raw)) return null;

        // Unix timestamp (date field da Kommo vem assim).
        if (long.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var num) && num > 0)
        {
            try
            {
                return num > 99999999999L
                    ? DateTimeOffset.FromUnixTimeMilliseconds(num).UtcDateTime
                    : DateTimeOffset.FromUnixTimeSeconds(num).UtcDateTime;
            }
            catch { /* overflow — segue pros parsers de string */ }
        }

        if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var d1)) return d1;
        if (DateTime.TryParse(raw, new CultureInfo("pt-BR"), DateTimeStyles.AssumeLocal, out var d2)) return d2.ToUniversalTime();
        return null;
    }

    /// <summary>
    /// Garante que existe uma <see cref="RecoveryAttempt"/> pra cada valor presente no
    /// custom field "Tentativas de resgastes" do lead. Dedup por (LeadId, Outcome) —
    /// rodar de novo não duplica. EntrySource="webhook" pra distinguir do backfill
    /// (events_api). CreatedAt = updated_at do lead na Kommo (não DateTime.UtcNow).
    /// </summary>
    private async Task UpsertRecoveryAttemptsAsync(Lead lead, int tenantId, DateTime attemptAt, CancellationToken ct)
    {
        if (lead.Id <= 0) return;
        var values = ExtractMultiselectValues(lead.CustomFieldsJson,
            n => n.Contains("tentativ") && n.Contains("resga"));
        if (values.Count == 0) return;

        var existing = await _db.RecoveryAttempts.AsNoTracking()
            .Where(r => r.LeadId == lead.Id)
            .Select(r => r.Outcome)
            .ToListAsync(ct);
        var existingSet = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);

        foreach (var raw in values)
        {
            var outcome = raw.Trim();
            if (outcome.Length == 0 || existingSet.Contains(outcome)) continue;
            _db.RecoveryAttempts.Add(new RecoveryAttempt
            {
                LeadId = lead.Id,
                TenantId = tenantId,
                Method = "resgate",
                Outcome = outcome,
                CreatedAt = DateTime.SpecifyKind(attemptAt, DateTimeKind.Utc),
                EntrySource = "webhook",
            });
            existingSet.Add(outcome);
        }
    }

    /// <summary>
    /// Lê todos os valores de um custom field multi-select (cada item de values[] vira
    /// uma string). Match por nome (lowercase). Tolerante a formatos: values[] de objetos
    /// com .value, values[] de strings cruas, ou value direto.
    /// </summary>
    private static List<string> ExtractMultiselectValues(string? customFieldsJson, Func<string, bool> nameMatches)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(customFieldsJson)) return result;
        try
        {
            using var doc = JsonDocument.Parse(customFieldsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return result;
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                if (el.ValueKind != JsonValueKind.Object) continue;
                if (!el.TryGetProperty("field_name", out var fn) || fn.ValueKind != JsonValueKind.String) continue;
                var name = fn.GetString()?.ToLowerInvariant() ?? "";
                if (!nameMatches(name)) continue;

                if (el.TryGetProperty("values", out var vals) && vals.ValueKind == JsonValueKind.Array)
                {
                    foreach (var v in vals.EnumerateArray())
                    {
                        string? s = null;
                        if (v.ValueKind == JsonValueKind.Object && v.TryGetProperty("value", out var inner))
                        {
                            s = inner.ValueKind == JsonValueKind.String ? inner.GetString()
                              : inner.ValueKind == JsonValueKind.Number ? inner.GetRawText()
                              : null;
                        }
                        else if (v.ValueKind == JsonValueKind.String) s = v.GetString();
                        else if (v.ValueKind == JsonValueKind.Number) s = v.GetRawText();
                        if (!string.IsNullOrWhiteSpace(s)) result.Add(s!);
                    }
                }
                else if (el.TryGetProperty("value", out var direct))
                {
                    var s = direct.ValueKind == JsonValueKind.String ? direct.GetString()
                          : direct.ValueKind == JsonValueKind.Number ? direct.GetRawText()
                          : null;
                    if (!string.IsNullOrWhiteSpace(s)) result.Add(s!);
                }
                return result;
            }
        }
        catch (JsonException) { /* ignora json malformado */ }
        return result;
    }

    /// <summary>Extrai um decimal de um custom field (aceita "1.500,00" e "1500.00").</summary>
    internal static decimal? TryExtractDecimalFromCustomFields(
        string? customFieldsJson, long? fieldId, Func<string, bool> nameMatches)
    {
        var raw = ExtractFieldRaw(customFieldsJson, fieldId, nameMatches);
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var s = raw.Trim();
        if (decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var v1)) return v1;
        if (decimal.TryParse(s, NumberStyles.Any, new CultureInfo("pt-BR"), out var v2)) return v2;
        return null;
    }

    /// <summary>
    /// Lê o "value" de um custom field do CustomFieldsJson (formato persistido pelo
    /// KommoFormParser/webhook: array de objetos com field_id + field_name + values[].value).
    /// Match por field_id quando disponível, senão por predicado de nome (lowercase).
    /// </summary>
    private static string? ExtractFieldRaw(string? customFieldsJson, long? fieldId, Func<string, bool> nameMatches)
    {
        if (string.IsNullOrWhiteSpace(customFieldsJson)) return null;
        try
        {
            using var doc = JsonDocument.Parse(customFieldsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                if (el.ValueKind != JsonValueKind.Object) continue;

                bool match;
                if (fieldId is not null)
                {
                    if (!el.TryGetProperty("field_id", out var fid)) continue;
                    long idv = fid.ValueKind == JsonValueKind.Number ? fid.GetInt64()
                             : (fid.ValueKind == JsonValueKind.String && long.TryParse(fid.GetString(), out var parsed) ? parsed : 0);
                    match = idv == fieldId.Value;
                }
                else
                {
                    if (!el.TryGetProperty("field_name", out var fn) || fn.ValueKind != JsonValueKind.String) continue;
                    var name = fn.GetString()?.ToLowerInvariant() ?? "";
                    match = nameMatches(name);
                }
                if (!match) continue;

                if (el.TryGetProperty("value", out var direct))
                {
                    if (direct.ValueKind == JsonValueKind.String) return direct.GetString();
                    if (direct.ValueKind == JsonValueKind.Number) return direct.GetRawText();
                }
                if (el.TryGetProperty("values", out var vals) && vals.ValueKind == JsonValueKind.Array)
                {
                    foreach (var v in vals.EnumerateArray())
                    {
                        if (v.ValueKind == JsonValueKind.Object && v.TryGetProperty("value", out var inner))
                        {
                            if (inner.ValueKind == JsonValueKind.String) return inner.GetString();
                            if (inner.ValueKind == JsonValueKind.Number) return inner.GetRawText();
                        }
                        else if (v.ValueKind == JsonValueKind.String) return v.GetString();
                        else if (v.ValueKind == JsonValueKind.Number) return v.GetRawText();
                    }
                }
                return null;
            }
        }
        catch (JsonException) { /* ignora json malformado */ }
        return null;
    }
}
