using System.Text.Json;
using System.Text.Json.Serialization;

namespace LeadAnalytics.Api.DTOs.Kommo;

/// <summary>
/// Representa o corpo completo de um webhook do Kommo, já desserializado a partir
/// do formato <c>application/x-www-form-urlencoded</c> com notação de colchetes
/// aninhada (ex.: <c>leads[add][0][id]=123</c>).
///
/// O Kommo agrupa eventos por entidade (leads, contacts, task, …) e, dentro de
/// cada entidade, por ação (add, update, delete, restore, status, responsible).
/// Cada propriedade abaixo é opcional — só vem preenchida a ação que disparou o hook.
/// </summary>
public class KommoWebhookPayload
{
    [JsonPropertyName("leads")] public KommoLeadsEnvelope? Leads { get; set; }

    /// <summary>Contatos E empresas chegam aqui; distinga pelo campo <c>type</c> (contact|company).</summary>
    [JsonPropertyName("contacts")] public KommoContactsEnvelope? Contacts { get; set; }

    [JsonPropertyName("task")] public KommoTasksEnvelope? Task { get; set; }
    [JsonPropertyName("unsorted")] public KommoUnsortedEnvelope? Unsorted { get; set; }
    [JsonPropertyName("catalogs")] public KommoCatalogsEnvelope? Catalogs { get; set; }
    [JsonPropertyName("message")] public KommoMessagesEnvelope? Message { get; set; }
    [JsonPropertyName("talk")] public KommoTalksEnvelope? Talk { get; set; }
    [JsonPropertyName("account")] public KommoAccount? Account { get; set; }
}

// ─────────────────────────────── LEADS ───────────────────────────────

public class KommoLeadsEnvelope
{
    [JsonPropertyName("add")] public List<KommoLead>? Add { get; set; }
    [JsonPropertyName("update")] public List<KommoLead>? Update { get; set; }
    [JsonPropertyName("delete")] public List<KommoLead>? Delete { get; set; }
    [JsonPropertyName("restore")] public List<KommoLead>? Restore { get; set; }
    [JsonPropertyName("status")] public List<KommoLead>? Status { get; set; }
    [JsonPropertyName("responsible")] public List<KommoLead>? Responsible { get; set; }
    [JsonPropertyName("note")] public List<KommoNoteWrapper>? Note { get; set; }
}

public class KommoLead
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("status_id")] public string? StatusId { get; set; }
    [JsonPropertyName("old_status_id")] public string? OldStatusId { get; set; }
    [JsonPropertyName("price")] public string? Price { get; set; }
    [JsonPropertyName("responsible_user_id")] public string? ResponsibleUserId { get; set; }
    [JsonPropertyName("old_responsible_user_id")] public string? OldResponsibleUserId { get; set; }
    [JsonPropertyName("pipeline_id")] public string? PipelineId { get; set; }
    [JsonPropertyName("account_id")] public string? AccountId { get; set; }
    [JsonPropertyName("created_user_id")] public string? CreatedUserId { get; set; }
    [JsonPropertyName("modified_user_id")] public string? ModifiedUserId { get; set; }
    [JsonPropertyName("created_at")] public string? CreatedAt { get; set; }
    [JsonPropertyName("updated_at")] public string? UpdatedAt { get; set; }
    [JsonPropertyName("custom_fields")] public List<KommoCustomField>? CustomFields { get; set; }
}

// ─────────────────────────── CONTATOS / EMPRESAS ──────────────────────

public class KommoContactsEnvelope
{
    [JsonPropertyName("add")] public List<KommoContact>? Add { get; set; }
    [JsonPropertyName("update")] public List<KommoContact>? Update { get; set; }
    [JsonPropertyName("delete")] public List<KommoContact>? Delete { get; set; }
    [JsonPropertyName("restore")] public List<KommoContact>? Restore { get; set; }
    [JsonPropertyName("note")] public List<KommoNoteWrapper>? Note { get; set; }
}

public class KommoContact
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("responsible_user_id")] public string? ResponsibleUserId { get; set; }
    [JsonPropertyName("old_responsible_user_id")] public string? OldResponsibleUserId { get; set; }
    [JsonPropertyName("account_id")] public string? AccountId { get; set; }
    [JsonPropertyName("created_at")] public string? CreatedAt { get; set; }
    [JsonPropertyName("updated_at")] public string? UpdatedAt { get; set; }
    /// <summary>"contact" ou "company" — chave de compatibilidade retroativa do Kommo.</summary>
    [JsonPropertyName("type")] public string? Type { get; set; }
    [JsonPropertyName("custom_fields")] public List<KommoCustomField>? CustomFields { get; set; }
}

// ─────────────────────────────── TAREFAS ──────────────────────────────

public class KommoTasksEnvelope
{
    [JsonPropertyName("add")] public List<KommoTask>? Add { get; set; }
    [JsonPropertyName("update")] public List<KommoTask>? Update { get; set; }
    [JsonPropertyName("delete")] public List<KommoTask>? Delete { get; set; }
}

public class KommoTask
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("element_id")] public string? ElementId { get; set; }
    [JsonPropertyName("element_type")] public string? ElementType { get; set; }
    [JsonPropertyName("task_type")] public string? TaskType { get; set; }
    [JsonPropertyName("text")] public string? Text { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("responsible_user_id")] public string? ResponsibleUserId { get; set; }
    [JsonPropertyName("old_responsible_user_id")] public string? OldResponsibleUserId { get; set; }
    [JsonPropertyName("account_id")] public string? AccountId { get; set; }
    [JsonPropertyName("complete_till")] public string? CompleteTill { get; set; }
    [JsonPropertyName("created_at")] public string? CreatedAt { get; set; }
    [JsonPropertyName("updated_at")] public string? UpdatedAt { get; set; }
}

// ──────────────────────────────── NOTAS ───────────────────────────────

/// <summary>Wrapper de nota: o Kommo encapsula a nota num objeto <c>{ "note": {...}, "type": "contact" }</c>.</summary>
public class KommoNoteWrapper
{
    [JsonPropertyName("note")] public KommoNote? Note { get; set; }
    [JsonPropertyName("type")] public string? Type { get; set; }
}

public class KommoNote
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("text")] public string? Text { get; set; }
    [JsonPropertyName("note_type")] public string? NoteType { get; set; }
    [JsonPropertyName("element_id")] public string? ElementId { get; set; }
    [JsonPropertyName("element_type")] public string? ElementType { get; set; }
    [JsonPropertyName("attachement")] public string? Attachement { get; set; }
    [JsonPropertyName("account_id")] public string? AccountId { get; set; }
    [JsonPropertyName("created_at")] public string? CreatedAt { get; set; }
}

// ──────────────────────── LEADS DE ENTRADA (UNSORTED) ─────────────────

public class KommoUnsortedEnvelope
{
    [JsonPropertyName("add")] public List<KommoUnsorted>? Add { get; set; }
    [JsonPropertyName("update")] public List<KommoUnsorted>? Update { get; set; }
    /// <summary>Aceitar/recusar um lead de entrada cai aqui (action = accept|decline).</summary>
    [JsonPropertyName("delete")] public List<KommoUnsorted>? Delete { get; set; }
}

public class KommoUnsorted
{
    [JsonPropertyName("uid")] public string? Uid { get; set; }
    [JsonPropertyName("category")] public string? Category { get; set; }
    [JsonPropertyName("pipeline_id")] public string? PipelineId { get; set; }
    [JsonPropertyName("account_id")] public string? AccountId { get; set; }
    [JsonPropertyName("lead_id")] public string? LeadId { get; set; }
    [JsonPropertyName("created_at")] public string? CreatedAt { get; set; }

    /// <summary>"accept" ou "decline" (somente em delete).</summary>
    [JsonPropertyName("action")] public string? Action { get; set; }
    [JsonPropertyName("accept_result")] public KommoUnsortedResult? AcceptResult { get; set; }
    [JsonPropertyName("decline_result")] public KommoUnsortedResult? DeclineResult { get; set; }
}

public class KommoUnsortedResult
{
    [JsonPropertyName("leads")] public List<string>? Leads { get; set; }
}

// ──────────────────────────── LISTA DE ELEMENTOS ──────────────────────

public class KommoCatalogsEnvelope
{
    [JsonPropertyName("add")] public List<KommoCatalogItem>? Add { get; set; }
    [JsonPropertyName("update")] public List<KommoCatalogItem>? Update { get; set; }
    [JsonPropertyName("delete")] public List<KommoCatalogItem>? Delete { get; set; }
}

public class KommoCatalogItem
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("catalog_id")] public string? CatalogId { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("type")] public string? Type { get; set; }
    [JsonPropertyName("custom_fields")] public List<KommoCustomField>? CustomFields { get; set; }
}

// ──────────────────────── MENSAGENS E CONVERSAS ───────────────────────

public class KommoMessagesEnvelope
{
    [JsonPropertyName("add")] public List<KommoMessage>? Add { get; set; }
}

public class KommoMessage
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("chat_id")] public string? ChatId { get; set; }
    [JsonPropertyName("talk_id")] public string? TalkId { get; set; }
    [JsonPropertyName("contact_id")] public string? ContactId { get; set; }
    [JsonPropertyName("text")] public string? Text { get; set; }
    [JsonPropertyName("entity_id")] public string? EntityId { get; set; }
    [JsonPropertyName("entity_type")] public string? EntityType { get; set; }
    [JsonPropertyName("type")] public string? Type { get; set; }
    [JsonPropertyName("origin")] public string? Origin { get; set; }
    [JsonPropertyName("created_at")] public string? CreatedAt { get; set; }
}

public class KommoTalksEnvelope
{
    [JsonPropertyName("add")] public List<KommoTalk>? Add { get; set; }
    [JsonPropertyName("update")] public List<KommoTalk>? Update { get; set; }
}

public class KommoTalk
{
    [JsonPropertyName("talk_id")] public string? TalkId { get; set; }
    [JsonPropertyName("contact_id")] public string? ContactId { get; set; }
    [JsonPropertyName("chat_id")] public string? ChatId { get; set; }
    [JsonPropertyName("entity_id")] public string? EntityId { get; set; }
    [JsonPropertyName("entity_type")] public string? EntityType { get; set; }
    [JsonPropertyName("is_in_work")] public string? IsInWork { get; set; }
    [JsonPropertyName("is_read")] public string? IsRead { get; set; }
    [JsonPropertyName("origin")] public string? Origin { get; set; }
    [JsonPropertyName("created_at")] public string? CreatedAt { get; set; }
    [JsonPropertyName("updated_at")] public string? UpdatedAt { get; set; }
}

// ─────────────────────────────── COMUNS ───────────────────────────────

public class KommoAccount
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("subdomain")] public string? Subdomain { get; set; }
}

/// <summary>
/// Campo personalizado do Kommo. <c>values</c> é polimórfico: pode ser uma lista de
/// objetos <c>{value, enum}</c>, uma lista de strings cruas (datas) ou um único objeto
/// (radiobutton). Por isso é mantido como <see cref="JsonElement"/> e lido por helpers.
/// </summary>
public class KommoCustomField
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("code")] public string? Code { get; set; }
    [JsonPropertyName("values")] public JsonElement? Values { get; set; }

    /// <summary>Extrai o primeiro valor textual do campo, qualquer que seja o formato.</summary>
    public string? FirstValue()
    {
        if (Values is not { } v) return null;

        return v.ValueKind switch
        {
            JsonValueKind.Array => v.GetArrayLength() == 0 ? null : ExtractFromElement(v[0]),
            JsonValueKind.Object => ExtractFromElement(v),
            JsonValueKind.String => v.GetString(),
            _ => null,
        };
    }

    private static string? ExtractFromElement(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.String => el.GetString(),
        JsonValueKind.Object => el.TryGetProperty("value", out var inner)
            ? (inner.ValueKind == JsonValueKind.String ? inner.GetString() : inner.ToString())
            : null,
        _ => el.ToString(),
    };
}
