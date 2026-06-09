using System.Text.Json;
using LeadAnalytics.Api.Models;
using LeadAnalytics.Api.Service.Stages;

namespace LeadAnalytics.Api.Service;

/// <summary>
/// Importa leads/contatos existentes da Kommo via REST e roda eles pelo
/// mesmo pipeline de ingestão dos webhooks (<see cref="KommoIngestionService"/>),
/// garantindo idempotência (ExternalId + TenantId é único, então sync e webhook
/// não duplicam o mesmo lead).
///
/// Limites: paginação de 250 por página da Kommo; cap default em 5000 leads
/// pra evitar requests longos. Pra contas maiores, basta rodar o sync de novo.
/// </summary>
public class KommoSyncService
{
    private const int PageSize = 250;
    private const string PhoneCode = "PHONE";
    private const string EmailCode = "EMAIL";

    private readonly KommoApiClient _api;
    private readonly KommoIngestionService _ingestion;
    private readonly ILogger<KommoSyncService> _logger;

    public KommoSyncService(
        KommoApiClient api,
        KommoIngestionService ingestion,
        ILogger<KommoSyncService> logger)
    {
        _api = api;
        _ingestion = ingestion;
        _logger = logger;
    }

    public async Task<KommoSyncResult> SyncAsync(
        Unit unit,
        string accessToken,
        int maxLeads,
        CancellationToken ct,
        bool skipRefetch = false)
    {
        if (string.IsNullOrWhiteSpace(unit.KommoSubdomain))
            throw new InvalidOperationException("A unidade precisa ter um KommoSubdomain configurado.");

        var result = new KommoSyncResult();
        var sw = System.Diagnostics.Stopwatch.StartNew();

        // 1) Coleta todos os leads paginando até o cap
        var leads = new List<KommoApiLead>();
        var contactIds = new HashSet<long>();
        var page = 1;
        while (leads.Count < maxLeads)
        {
            KommoLeadsPageResponse? resp;
            try
            {
                resp = await _api.GetLeadsPageAsync(unit.KommoSubdomain, accessToken, page, PageSize, ct);
            }
            catch (HttpRequestException ex)
            {
                result.Error = ex.Message;
                _logger.LogError(ex, "Falha na pagina {Page} de leads (unit {Unit})", page, unit.Id);
                break;
            }

            var batch = resp?.Embedded?.Leads;
            if (batch is null || batch.Count == 0) break;

            leads.AddRange(batch);
            foreach (var l in batch)
                foreach (var c in l.Embedded?.Contacts ?? [])
                    contactIds.Add(c.Id);

            result.PagesFetched = page;
            _logger.LogInformation(
                "Sync Kommo unit {Unit}: página {Page} → {Count} leads (acumulado {Total})",
                unit.Id, page, batch.Count, leads.Count);

            if (resp?.Links?.Next is null) break;
            page++;
        }

        result.LeadsFetched = leads.Count;
        if (leads.Count == 0)
        {
            sw.Stop();
            result.DurationMs = (int)sw.ElapsedMilliseconds;
            return result;
        }

        // 1.25) WORKAROUND DE BUG DA KOMMO:
        //       A paginated /api/v4/leads?limit=250 retorna `custom_fields_values: null`
        //       pra ~30% dos leads quando a resposta é grande, mesmo que o lead tenha
        //       campos preenchidos. Comprovado com /api/admin/custom-fields/test-sync-pipeline
        //       que retornou 176 com dados + 74 nulls num único page=1.
        //
        //       Solução: re-buscar individualmente cada lead com null via
        //       GET /api/v4/leads/{id} (confiável). Custo: ~150ms cada × N nulls.
        //       Pra unit típica isso é alguns minutos a mais — aceitável dado que
        //       é a única forma de não perder os dados das SDRs.
        var leadsWithNullCustomFields = leads.Where(l => l.CustomFieldsValues is null).ToList();
        if (leadsWithNullCustomFields.Count > 0 && !skipRefetch)
        {
            _logger.LogInformation(
                "Sync Kommo unit {Unit}: {N} leads com custom_fields_values=null — re-buscando individualmente",
                unit.Id, leadsWithNullCustomFields.Count);

            var refetched = 0;
            foreach (var lead in leadsWithNullCustomFields)
            {
                if (ct.IsCancellationRequested) break;
                try
                {
                    var full = await _api.GetLeadByIdAsync(unit.KommoSubdomain, accessToken, lead.Id, ct);
                    if (full?.CustomFieldsValues is { Count: > 0 })
                    {
                        lead.CustomFieldsValues = full.CustomFieldsValues;
                        refetched++;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Falha ao re-buscar lead {LeadId} (unit {Unit})", lead.Id, unit.Id);
                }
                // Respeita o rate-limit da Kommo (7 RPS = ~143ms entre requests)
                try { await Task.Delay(150, ct); }
                catch (OperationCanceledException) { break; }
            }
            _logger.LogInformation(
                "Sync Kommo unit {Unit}: re-buscou {N}/{Total} leads, {Recovered} recuperaram custom_fields",
                unit.Id, leadsWithNullCustomFields.Count, leadsWithNullCustomFields.Count, refetched);
        }

        // 1.5) Busca o SCHEMA de custom fields da conta (uma chamada só).
        //      Monta o mapa enumLabelByFieldEnum[fieldId][enumId] = "Instagram"
        //      pra resolver enum_id → texto humano nos selects/multiselects.
        var enumLabelByFieldEnum = new Dictionary<long, Dictionary<long, string>>();
        try
        {
            var schemaResp = await _api.GetCustomFieldsAsync(unit.KommoSubdomain, accessToken, ct);
            foreach (var def in schemaResp?.Embedded?.CustomFields ?? new())
            {
                if (def.Enums is null || def.Enums.Count == 0) continue;
                var map = new Dictionary<long, string>(def.Enums.Count);
                foreach (var en in def.Enums)
                    if (!string.IsNullOrWhiteSpace(en.Value)) map[en.Id] = en.Value!;
                if (map.Count > 0) enumLabelByFieldEnum[def.Id] = map;
            }
            _logger.LogInformation(
                "Sync Kommo unit {Unit}: schema carregado, {N} campos com enums",
                unit.Id, enumLabelByFieldEnum.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Sync Kommo unit {Unit}: falha ao buscar schema de custom_fields — selects vão cair em 'enum_<id>'",
                unit.Id);
        }

        // 1.75) Mapa de etapas derivado dos NOMES das etapas da Kommo (status_id → etapa
        //       canônica). Resolve agendados/etapas mesmo quando a unidade NÃO tem
        //       KommoStageMapJson preenchido — CanonicalStages.Resolve reconhece nomes
        //       como "04_AGENDADO_SEM_PAGAMENTO". O mapa explícito da unidade ainda vence.
        var stageNameMap = new Dictionary<string, string>();
        try
        {
            var pipes = await _api.GetPipelinesAsync(unit.KommoSubdomain, accessToken, ct);
            foreach (var p in pipes?.Embedded?.Pipelines ?? new())
                foreach (var st in p.Embedded?.Statuses ?? new())
                {
                    var canonical = CanonicalStages.Resolve(st.Name);
                    if (canonical != null) stageNameMap[st.Id.ToString()] = canonical;
                }
            _logger.LogInformation(
                "Sync Kommo unit {Unit}: {N} etapas resolvidas por nome (auto stage-map)",
                unit.Id, stageNameMap.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Sync Kommo unit {Unit}: falha ao buscar pipelines — sem auto-resolve de etapa por nome",
                unit.Id);
        }

        // 2) Busca os contatos em lotes (até 250 por chamada) pra pegar phone/email
        var contactById = new Dictionary<long, KommoApiContact>();
        foreach (var chunk in Chunk(contactIds, 250))
        {
            try
            {
                var resp = await _api.GetContactsByIdsAsync(unit.KommoSubdomain, accessToken, chunk, ct);
                foreach (var c in resp?.Embedded?.Contacts ?? [])
                    contactById[c.Id] = c;
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(ex, "Falha ao buscar lote de contatos (unit {Unit}) — continuando sem", unit.Id);
            }
        }
        result.ContactsFetched = contactById.Count;

        // 3) Converte cada lead em LeadEvent (mesmo shape que o webhook produz)
        //    e despacha pelo KommoIngestionService — mesmo pipeline, idempotente.
        var events = new List<LeadEvent>(leads.Count);
        foreach (var lead in leads)
        {
            if (lead.IsDeleted) continue;

            var (phone, email) = ExtractContactInfo(lead, contactById);

            events.Add(new LeadEvent
            {
                SourceSystem = "Kommo",
                EntityType = "lead",
                Action = "add",
                ExternalId = lead.Id.ToString(),
                Name = lead.Name,
                Stage = lead.StatusId?.ToString() ?? string.Empty,
                Price = lead.Price?.ToString(),
                AttendantId = lead.ResponsibleUserId?.ToString() ?? string.Empty,
                PipelineId = lead.PipelineId?.ToString(),
                AccountId = lead.AccountId?.ToString(),
                Phone = phone,
                Email = email,
                CustomFieldsJson = SerializeCustomFields(lead.CustomFieldsValues, enumLabelByFieldEnum),
                TagsJson = SerializeTags(lead.Embedded?.Tags),
                // Data REAL da criação na Kommo (unix → UTC). Sobrescreve o
                // CreatedAt do nosso banco que estava virando 'data do 1º sync'.
                KommoCreatedAtUtc = lead.CreatedAt.HasValue
                    ? DateTimeOffset.FromUnixTimeSeconds(lead.CreatedAt.Value).UtcDateTime
                    : null,
                // Última modificação real na Kommo — vira ChangedAt do histórico
                // de etapa. Melhor aproximação possível pra "quando o lead entrou
                // na etapa" via sync (sem precisar bater em /leads/{id}/events).
                // Se o lead foi modificado depois da mudança de etapa, vai pra
                // data do último update — ainda muito melhor que DateTime.UtcNow
                // do sync, que jogaria leads agendados ontem no KPI de hoje.
                KommoModifiedAtUtc = lead.UpdatedAt.HasValue
                    ? DateTimeOffset.FromUnixTimeSeconds(lead.UpdatedAt.Value).UtcDateTime
                    : null,
            });
        }

        result.LeadsPersisted = await _ingestion.IngestAsync(events, unit, ct, stageNameMap);

        sw.Stop();
        result.DurationMs = (int)sw.ElapsedMilliseconds;
        return result;
    }

    private static (string Phone, string? Email) ExtractContactInfo(
        KommoApiLead lead, Dictionary<long, KommoApiContact> contactsById)
    {
        // Custom fields do PRÓPRIO lead (algumas contas usam PHONE/EMAIL no lead)
        var leadPhone = FindCustomField(lead.CustomFieldsValues, PhoneCode);
        var leadEmail = FindCustomField(lead.CustomFieldsValues, EmailCode);
        if (!string.IsNullOrWhiteSpace(leadPhone) || !string.IsNullOrWhiteSpace(leadEmail))
            return (leadPhone ?? string.Empty, leadEmail);

        // Senão, tenta no contato principal (ou no primeiro)
        var contactRef = lead.Embedded?.Contacts?.FirstOrDefault(c => c.IsMain)
                      ?? lead.Embedded?.Contacts?.FirstOrDefault();
        if (contactRef is null) return (string.Empty, null);
        if (!contactsById.TryGetValue(contactRef.Id, out var contact)) return (string.Empty, null);

        var phone = FindCustomField(contact.CustomFieldsValues, PhoneCode) ?? string.Empty;
        var email = FindCustomField(contact.CustomFieldsValues, EmailCode);
        return (phone, email);
    }

    private static string? FindCustomField(List<KommoApiCustomField>? fields, string code)
    {
        if (fields is null) return null;
        var field = fields.FirstOrDefault(f =>
            string.Equals(f.FieldCode, code, StringComparison.OrdinalIgnoreCase));
        return field?.Values?.FirstOrDefault()?.GetStringValue();
    }

    /// <summary>
    /// Serializa os custom fields da Kommo num array JSON enxuto. Pra cada
    /// <c>KommoApiCustomFieldValue</c> resolve o label na ordem:
    /// <c>value</c> → schema (enumLabelByFieldEnum[field][enumId]) → enum_code → "enum_<id>".
    /// Multiselect junta os labels resolvidos com ", ".
    /// </summary>
    private static string? SerializeCustomFields(
        List<KommoApiCustomField>? fields,
        Dictionary<long, Dictionary<long, string>> enumLabelByFieldEnum)
    {
        if (fields is null || fields.Count == 0) return null;

        var slim = fields.Select(f =>
        {
            enumLabelByFieldEnum.TryGetValue(f.FieldId, out var enumMap);

            // Resolve cada item da lista values (multiselect pode ter vários).
            var labels = f.Values?
                .Select(v => ResolveValueLabel(v, enumMap))
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToList();

            var value = labels is { Count: > 0 } ? string.Join(", ", labels!) : null;

            var enumId = f.Values?.Select(v => v.EnumId).FirstOrDefault(id => id is > 0);
            var enumCode = f.Values?.Select(v => v.EnumCode).FirstOrDefault(c => !string.IsNullOrWhiteSpace(c));

            return new
            {
                field_id = f.FieldId,
                field_name = f.FieldName,
                field_code = f.FieldCode,
                type = f.FieldType,
                value,
                enum_id = enumId,
                enum_code = enumCode,
            };
        }).ToList();
        return JsonSerializer.Serialize(slim);
    }

    /// <summary>
    /// Resolve o label de UM <see cref="KommoApiCustomFieldValue"/> usando, em ordem:
    /// (1) o próprio Value quando preenchido; (2) o schema da conta
    /// (enumMap[enum_id] = label); (3) enum_code; (4) "enum_{id}" como último recurso.
    /// </summary>
    private static string? ResolveValueLabel(KommoApiCustomFieldValue v, Dictionary<long, string>? enumMap)
    {
        var raw = v.GetStringValueRawOnly();
        if (!string.IsNullOrWhiteSpace(raw)) return raw;

        if (v.EnumId is long id && id > 0)
        {
            if (enumMap is not null && enumMap.TryGetValue(id, out var label) && !string.IsNullOrWhiteSpace(label))
                return label;
            if (!string.IsNullOrWhiteSpace(v.EnumCode)) return v.EnumCode;
            return $"enum_{id}";
        }
        if (!string.IsNullOrWhiteSpace(v.EnumCode)) return v.EnumCode;
        return null;
    }

    private static string? SerializeTags(List<KommoApiTag>? tags)
    {
        if (tags is null || tags.Count == 0) return null;
        var names = tags.Where(t => !string.IsNullOrWhiteSpace(t.Name)).Select(t => t.Name!).ToList();
        if (names.Count == 0) return null;
        return JsonSerializer.Serialize(names);
    }

    private static IEnumerable<IEnumerable<T>> Chunk<T>(IEnumerable<T> source, int size)
    {
        var chunk = new List<T>(size);
        foreach (var item in source)
        {
            chunk.Add(item);
            if (chunk.Count >= size)
            {
                yield return chunk;
                chunk = new List<T>(size);
            }
        }
        if (chunk.Count > 0) yield return chunk;
    }
}

public class KommoSyncResult
{
    public int PagesFetched { get; set; }
    public int LeadsFetched { get; set; }
    public int ContactsFetched { get; set; }
    public int LeadsPersisted { get; set; }
    public int DurationMs { get; set; }
    public string? Error { get; set; }
}
