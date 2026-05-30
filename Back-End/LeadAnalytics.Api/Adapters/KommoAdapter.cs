using LeadAnalytics.Api.DTOs.Kommo;
using LeadAnalytics.Api.Models;

namespace LeadAnalytics.Api.Adapters;

/// <summary>
/// Traduz um <see cref="KommoWebhookPayload"/> (já desserializado do form-urlencoded)
/// para uma sequência de <see cref="LeadEvent"/> normalizados — um por entidade afetada.
/// </summary>
public class KommoAdapter
{
    private const string Source = "Kommo";

    // Códigos de campo personalizado padrão do Kommo.
    private const string PhoneCode = "PHONE";
    private const string EmailCode = "EMAIL";

    public IReadOnlyList<LeadEvent> ToLeadEvents(KommoWebhookPayload payload)
    {
        var events = new List<LeadEvent>();
        var accountId = payload.Account?.Id;

        MapLeads(payload.Leads, events, accountId);
        MapContacts(payload.Contacts, events, accountId);
        MapTasks(payload.Task, events, accountId);
        MapUnsorted(payload.Unsorted, events, accountId);
        MapNotes(payload.Leads?.Note, "lead", events, accountId);
        MapNotes(payload.Contacts?.Note, "contact", events, accountId);

        return events;
    }

    private static void MapLeads(KommoLeadsEnvelope? env, List<LeadEvent> sink, string? accountId)
    {
        if (env is null) return;

        AddLeads(env.Add, "add", sink, accountId);
        AddLeads(env.Update, "update", sink, accountId);
        AddLeads(env.Delete, "delete", sink, accountId);
        AddLeads(env.Restore, "restore", sink, accountId);
        AddLeads(env.Status, "status", sink, accountId);
        AddLeads(env.Responsible, "responsible", sink, accountId);
    }

    private static void AddLeads(List<KommoLead>? leads, string action, List<LeadEvent> sink, string? accountId)
    {
        if (leads is null) return;

        foreach (var lead in leads)
        {
            sink.Add(new LeadEvent
            {
                SourceSystem = Source,
                EntityType = "lead",
                Action = action,
                ExternalId = lead.Id ?? string.Empty,
                Name = lead.Name,
                Stage = lead.StatusId ?? string.Empty,
                OldStage = lead.OldStatusId,
                Price = lead.Price,
                AttendantId = lead.ResponsibleUserId ?? string.Empty,
                OldAttendantId = lead.OldResponsibleUserId,
                PipelineId = lead.PipelineId,
                AccountId = lead.AccountId ?? accountId,
                Phone = FindCustomField(lead.CustomFields, PhoneCode) ?? string.Empty,
                Email = FindCustomField(lead.CustomFields, EmailCode),
            });
        }
    }

    private static void MapContacts(KommoContactsEnvelope? env, List<LeadEvent> sink, string? accountId)
    {
        if (env is null) return;

        AddContacts(env.Add, "add", sink, accountId);
        AddContacts(env.Update, "update", sink, accountId);
        AddContacts(env.Delete, "delete", sink, accountId);
        AddContacts(env.Restore, "restore", sink, accountId);
    }

    private static void AddContacts(List<KommoContact>? contacts, string action, List<LeadEvent> sink, string? accountId)
    {
        if (contacts is null) return;

        foreach (var c in contacts)
        {
            sink.Add(new LeadEvent
            {
                SourceSystem = Source,
                // O Kommo manda contatos e empresas no mesmo envelope; type diferencia.
                EntityType = c.Type == "company" ? "company" : "contact",
                Action = action,
                ExternalId = c.Id ?? string.Empty,
                Name = c.Name,
                AttendantId = c.ResponsibleUserId ?? string.Empty,
                OldAttendantId = c.OldResponsibleUserId,
                AccountId = c.AccountId ?? accountId,
                Phone = FindCustomField(c.CustomFields, PhoneCode) ?? string.Empty,
                Email = FindCustomField(c.CustomFields, EmailCode),
            });
        }
    }

    private static void MapTasks(KommoTasksEnvelope? env, List<LeadEvent> sink, string? accountId)
    {
        if (env is null) return;

        AddTasks(env.Add, "add", sink, accountId);
        AddTasks(env.Update, "update", sink, accountId);
        AddTasks(env.Delete, "delete", sink, accountId);
    }

    private static void AddTasks(List<KommoTask>? tasks, string action, List<LeadEvent> sink, string? accountId)
    {
        if (tasks is null) return;

        foreach (var t in tasks)
        {
            sink.Add(new LeadEvent
            {
                SourceSystem = Source,
                EntityType = "task",
                Action = action,
                ExternalId = t.Id ?? string.Empty,
                Name = t.Text,
                AttendantId = t.ResponsibleUserId ?? string.Empty,
                OldAttendantId = t.OldResponsibleUserId,
                AccountId = t.AccountId ?? accountId,
            });
        }
    }

    private static void MapUnsorted(KommoUnsortedEnvelope? env, List<LeadEvent> sink, string? accountId)
    {
        if (env is null) return;

        AddUnsorted(env.Add, "add", sink, accountId);
        AddUnsorted(env.Update, "update", sink, accountId);

        // Em "delete", aceitar/recusar são distinguidos por Action + *_result.
        // Quando o usuário ACEITA, o Kommo manda os IDs reais dos leads criados —
        // tratamos cada um como um lead "add" pra entrarem no nosso banco.
        foreach (var u in env.Delete ?? [])
        {
            var action = u.Action ?? "delete"; // accept | decline | delete

            var acceptedLeadIds = u.AcceptResult?.Leads;
            if (acceptedLeadIds is { Count: > 0 })
            {
                foreach (var leadId in acceptedLeadIds)
                {
                    sink.Add(new LeadEvent
                    {
                        SourceSystem = Source,
                        EntityType = "lead",
                        Action = "add",
                        ExternalId = leadId,
                        PipelineId = u.PipelineId,
                        AccountId = u.AccountId ?? accountId,
                    });
                }
                continue;
            }

            // Recusado ou delete puro — só log/auditoria.
            var declinedOrJustDeleted = u.DeclineResult?.Leads?.FirstOrDefault()
                ?? u.LeadId
                ?? u.Uid
                ?? string.Empty;

            sink.Add(new LeadEvent
            {
                SourceSystem = Source,
                EntityType = "unsorted",
                Action = action,
                ExternalId = declinedOrJustDeleted,
                PipelineId = u.PipelineId,
                AccountId = u.AccountId ?? accountId,
            });
        }
    }

    private static void AddUnsorted(List<KommoUnsorted>? items, string action, List<LeadEvent> sink, string? accountId)
    {
        if (items is null) return;

        foreach (var u in items)
        {
            // Se o unsorted já traz um lead_id NUMÉRICO da Kommo, trata como
            // evento de lead — assim o lead aparece no dashboard mesmo antes
            // de ser triado/aceito manualmente na Kommo.
            if (!string.IsNullOrWhiteSpace(u.LeadId)
                && int.TryParse(u.LeadId, out var numericId) && numericId > 0)
            {
                sink.Add(new LeadEvent
                {
                    SourceSystem = Source,
                    EntityType = "lead",
                    Action = action,
                    ExternalId = u.LeadId,
                    PipelineId = u.PipelineId,
                    AccountId = u.AccountId ?? accountId,
                });
                continue;
            }

            // Sem lead_id ainda: só guarda como evento "unsorted" pra log/audit.
            sink.Add(new LeadEvent
            {
                SourceSystem = Source,
                EntityType = "unsorted",
                Action = action,
                ExternalId = u.Uid ?? string.Empty,
                PipelineId = u.PipelineId,
                AccountId = u.AccountId ?? accountId,
            });
        }
    }

    private static void MapNotes(List<KommoNoteWrapper>? notes, string defaultEntity, List<LeadEvent> sink, string? accountId)
    {
        if (notes is null) return;

        foreach (var wrapper in notes)
        {
            var note = wrapper.Note;
            if (note is null) continue;

            sink.Add(new LeadEvent
            {
                SourceSystem = Source,
                EntityType = "note",
                Action = "note",
                ExternalId = note.ElementId ?? note.Id ?? string.Empty,
                Name = note.Text,
                AccountId = note.AccountId ?? accountId,
            });
        }
    }

    private static string? FindCustomField(List<KommoCustomField>? fields, string code)
        => fields?.FirstOrDefault(f => string.Equals(f.Code, code, StringComparison.OrdinalIgnoreCase))?.FirstValue();
}
