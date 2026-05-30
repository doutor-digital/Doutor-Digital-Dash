using System.Text.Json;
using LeadAnalytics.Api.Models;

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
        CancellationToken ct)
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
                CustomFieldsJson = SerializeCustomFields(lead.CustomFieldsValues),
                TagsJson = SerializeTags(lead.Embedded?.Tags),
            });
        }

        result.LeadsPersisted = await _ingestion.IngestAsync(events, unit, ct);

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
    /// Serializa os custom fields da Kommo num array JSON enxuto. Pega só
    /// o primeiro value (a Kommo permite múltiplos mas raramente são usados).
    /// </summary>
    private static string? SerializeCustomFields(List<KommoApiCustomField>? fields)
    {
        if (fields is null || fields.Count == 0) return null;
        var slim = fields.Select(f => new
        {
            field_id = f.FieldId,
            field_name = f.FieldName,
            field_code = f.FieldCode,
            type = f.FieldType,
            value = f.Values?.FirstOrDefault()?.GetStringValue(),
        }).ToList();
        return JsonSerializer.Serialize(slim);
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
