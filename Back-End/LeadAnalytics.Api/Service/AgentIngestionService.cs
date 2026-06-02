using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace LeadAnalytics.Api.Service;

/// <summary>
/// Recebe o payload do agente-Dt (conversa completa) e faz upsert de
/// <see cref="AgentConversation"/> + re-sincroniza as <see cref="AgentMessage"/>.
/// Identidade estável por (TenantId, ExternalId). Tenta vincular a conversa ao
/// Contact/Lead existente pelo telefone.
/// </summary>
public class AgentIngestionService(AppDbContext db, ILogger<AgentIngestionService> logger)
{
    private readonly AppDbContext _db = db;
    private readonly ILogger<AgentIngestionService> _logger = logger;

    public async Task<AgentIngestResult> IngestAsync(AgentWebhookPayload payload, Unit unit, CancellationToken ct)
    {
        var externalId = (payload.ConversationId ?? payload.Id)?.Trim();
        if (string.IsNullOrWhiteSpace(externalId))
            throw new ArgumentException("conversationId é obrigatório no payload.");

        var tenantId = unit.ClinicId;
        var now = DateTime.UtcNow;

        var phoneRaw = payload.Contact?.Phone?.Trim();
        var digits = phoneRaw is null ? null : OnlyDigits(phoneRaw);

        // Carrega (ou cria) a conversa pelo par estável (tenant, externalId).
        var conv = await _db.AgentConversations
            .Include(c => c.Messages)
            .FirstOrDefaultAsync(c => c.TenantId == tenantId && c.ExternalId == externalId, ct);

        var isNew = conv is null;
        if (conv is null)
        {
            conv = new AgentConversation
            {
                TenantId = tenantId,
                ExternalId = externalId,
                CreatedAt = now,
            };
            _db.AgentConversations.Add(conv);
        }

        conv.UnitId = unit.Id;
        conv.AgentName = payload.Agent ?? conv.AgentName ?? "agente-Dt";
        conv.Channel = payload.Channel ?? conv.Channel ?? "whatsapp";
        conv.ContactName = payload.Contact?.Name ?? conv.ContactName;
        if (!string.IsNullOrWhiteSpace(phoneRaw)) conv.ContactPhone = phoneRaw;
        if (!string.IsNullOrWhiteSpace(digits)) conv.PhoneNormalized = digits;
        conv.Summary = payload.Summary ?? conv.Summary;
        conv.Intent = payload.Intent ?? conv.Intent;
        conv.Sentiment = payload.Sentiment ?? conv.Sentiment;
        conv.UpdatedAt = now;

        if (payload.Handoff == true && !conv.HandedOff)
        {
            conv.HandedOff = true;
            conv.HandoffAt = now;
        }

        // Status: explícito > derivado (handoff/closed) > mantém.
        conv.Status = payload.Status?.Trim().ToLowerInvariant() switch
        {
            "active" or "open" => "active",
            "closed" or "ended" or "finished" => "closed",
            "handoff" or "human" => "handoff",
            _ => conv.HandedOff ? "handoff" : payload.EndedAt.HasValue ? "closed" : conv.Status,
        };

        if (payload.Tokens is not null)
        {
            conv.TokensIn = payload.Tokens.In ?? conv.TokensIn;
            conv.TokensOut = payload.Tokens.Out ?? conv.TokensOut;
        }

        if (payload.Metadata is { ValueKind: not JsonValueKind.Undefined and not JsonValueKind.Null } meta)
            conv.MetadataJson = meta.GetRawText();

        // ─── Mensagens: re-sincroniza (conversa completa) ───────────────────
        var messages = payload.Messages ?? [];
        if (!isNew && conv.Messages.Count > 0)
            _db.AgentMessages.RemoveRange(conv.Messages);

        var rebuilt = new List<AgentMessage>(messages.Count);
        DateTime? firstAt = null, lastAt = null;
        var seq = 0;
        foreach (var m in messages)
        {
            var at = m.At ?? m.Timestamp ?? now.AddSeconds(seq);
            firstAt = firstAt is null || at < firstAt ? at : firstAt;
            lastAt = lastAt is null || at > lastAt ? at : lastAt;

            rebuilt.Add(new AgentMessage
            {
                Conversation = conv,
                Role = NormalizeRole(m.Role),
                Content = m.Content,
                SentAt = at,
                ExternalId = m.Id,
                ToolName = m.ToolName,
                MetadataJson = m.Metadata is { ValueKind: not JsonValueKind.Undefined and not JsonValueKind.Null } mm
                    ? mm.GetRawText()
                    : null,
                CreatedAt = now,
            });
            seq++;
        }
        conv.Messages = rebuilt;
        conv.MessageCount = rebuilt.Count;

        if (conv.StartedAt == default)
            conv.StartedAt = payload.StartedAt ?? firstAt ?? now;
        else if (payload.StartedAt.HasValue)
            conv.StartedAt = payload.StartedAt.Value;
        conv.FirstMessageAt = firstAt ?? conv.FirstMessageAt;
        conv.LastMessageAt = lastAt ?? conv.LastMessageAt;
        if (payload.EndedAt.HasValue) conv.EndedAt = payload.EndedAt;
        else if (conv.Status == "closed" && conv.EndedAt is null) conv.EndedAt = lastAt ?? now;

        // ─── Vínculo com Contact / Lead pelo telefone ───────────────────────
        if (!string.IsNullOrWhiteSpace(digits))
            await LinkByPhoneAsync(conv, tenantId, phoneRaw!, digits!, ct);

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "🤖 Conversa I.A. {Op} | tenant={Tenant} unit={Unit} conv={Ext} msgs={Count} contact={Contact} lead={Lead}",
            isNew ? "criada" : "atualizada", tenantId, unit.Id, externalId, rebuilt.Count, conv.ContactId, conv.LeadId);

        return new AgentIngestResult(conv.Id, isNew, rebuilt.Count);
    }

    private async Task LinkByPhoneAsync(
        AgentConversation conv, int tenantId, string phoneRaw, string digits, CancellationToken ct)
    {
        var core = digits.Length >= 8 ? digits[^8..] : digits;

        if (conv.ContactId is null)
        {
            var contactId = await _db.Contacts.AsNoTracking()
                .Where(c => c.TenantId == tenantId &&
                            (c.PhoneNormalized == digits ||
                             (core != "" && c.PhoneNormalized != null && c.PhoneNormalized.EndsWith(core))))
                .OrderByDescending(c => c.CreatedAt)
                .Select(c => (int?)c.Id)
                .FirstOrDefaultAsync(ct);
            if (contactId.HasValue) conv.ContactId = contactId;
        }

        if (conv.LeadId is null)
        {
            var leadId = await _db.Leads.AsNoTracking()
                .Where(l => l.TenantId == tenantId &&
                            (l.Phone == phoneRaw || l.Phone == digits ||
                             (core != "" && l.Phone.Contains(core))))
                .OrderByDescending(l => l.CreatedAt)
                .Select(l => (int?)l.Id)
                .FirstOrDefaultAsync(ct);
            if (leadId.HasValue) conv.LeadId = leadId;
        }
    }

    private static string NormalizeRole(string? role) => role?.Trim().ToLowerInvariant() switch
    {
        "assistant" or "ai" or "bot" or "agent" => "assistant",
        "system" => "system",
        "tool" or "function" => "tool",
        _ => "user",
    };

    private static string OnlyDigits(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s) if (char.IsDigit(c)) sb.Append(c);
        return sb.ToString();
    }
}

public readonly record struct AgentIngestResult(int ConversationId, bool Created, int MessageCount);

// ─── Contrato do webhook do agente-Dt (eu defino) ───────────────────────────

public class AgentWebhookPayload
{
    /// <summary>Id estável da conversa (obrigatório). Aceita "conversationId" ou "id".</summary>
    [JsonPropertyName("conversationId")] public string? ConversationId { get; set; }
    [JsonPropertyName("id")] public string? Id { get; set; }

    [JsonPropertyName("agent")] public string? Agent { get; set; }
    [JsonPropertyName("channel")] public string? Channel { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }

    [JsonPropertyName("contact")] public AgentContactDto? Contact { get; set; }

    [JsonPropertyName("summary")] public string? Summary { get; set; }
    [JsonPropertyName("intent")] public string? Intent { get; set; }
    [JsonPropertyName("sentiment")] public string? Sentiment { get; set; }
    [JsonPropertyName("handoff")] public bool? Handoff { get; set; }

    [JsonPropertyName("startedAt")] public DateTime? StartedAt { get; set; }
    [JsonPropertyName("endedAt")] public DateTime? EndedAt { get; set; }

    [JsonPropertyName("tokens")] public AgentTokensDto? Tokens { get; set; }
    [JsonPropertyName("metadata")] public JsonElement? Metadata { get; set; }

    [JsonPropertyName("messages")] public List<AgentMessageDto> Messages { get; set; } = [];
}

public class AgentContactDto
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("phone")] public string? Phone { get; set; }
    [JsonPropertyName("externalId")] public string? ExternalId { get; set; }
}

public class AgentTokensDto
{
    [JsonPropertyName("in")] public int? In { get; set; }
    [JsonPropertyName("out")] public int? Out { get; set; }
}

public class AgentMessageDto
{
    [JsonPropertyName("role")] public string? Role { get; set; }
    [JsonPropertyName("content")] public string? Content { get; set; }
    /// <summary>Horário da mensagem. Aceita "at" ou "timestamp".</summary>
    [JsonPropertyName("at")] public DateTime? At { get; set; }
    [JsonPropertyName("timestamp")] public DateTime? Timestamp { get; set; }
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("toolName")] public string? ToolName { get; set; }
    [JsonPropertyName("metadata")] public JsonElement? Metadata { get; set; }
}
