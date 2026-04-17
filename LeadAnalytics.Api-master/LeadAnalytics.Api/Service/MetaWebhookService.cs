using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.DTOs;
using LeadAnalytics.Api.DTOs.Meta;
using LeadAnalytics.Api.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace LeadAnalytics.Api.Service;

public class MetaWebhookService(AppDbContext db, ILogger<MetaWebhookService> logger)
{
    private readonly AppDbContext _db = db;
    private readonly ILogger<MetaWebhookService> _logger = logger;

    public async Task<WebhookProcessResult> ProcessWebhookAsync(MetaWebhookDto webhook)
    {
        var eventsProcessed = 0;
        var originEventsCreated = 0;

        foreach (var entry in webhook.Entry)
        {
            foreach (var change in entry.Changes)
            {
                if (change.Field != "messages")
                    continue;

                var value = change.Value;

                // Processar cada mensagem
                if (value.Messages is not null)
                {
                    foreach (var message in value.Messages)
                    {
                        eventsProcessed++;

                        var phone = NormalizePhone(message.From);
                        var contactName = value.Contacts?.FirstOrDefault()?.Profile?.Name;

                        // 1️⃣ Salva webhook bruto (auditoria)
                        var webhookEvent = await SaveWebhookEventAsync(webhook, phone);

                        // 2️⃣ Extrai dados de referral (se existir)
                        if (message.Referral is not null)
                        {
                            var originEvent = await CreateOriginEventFromReferralAsync(
                                message.Referral,
                                phone,
                                contactName,
                                webhookEvent.Id);

                            originEventsCreated++;

                            _logger.LogInformation(
                                "✅ OriginEvent criado: Phone {Phone} → {SourceType} (CtwaClid: {Clid})",
                                phone, originEvent.SourceType, originEvent.CtwaClid);
                        }
                        else
                        {
                            // 3️⃣ Mensagem sem referral = entrada SOCIAL ou UNKNOWN
                            var originEvent = await CreateSocialOriginEventAsync(
                                phone,
                                contactName,
                                message,
                                webhookEvent.Id);

                            originEventsCreated++;

                            _logger.LogInformation(
                                "⚠️ OriginEvent SOCIAL criado: Phone {Phone} (sem referral)",
                                phone);
                        }
                    }
                }
            }
        }

        return new WebhookProcessResult
        {
            EventsProcessed = eventsProcessed,
            OriginEventsCreated = originEventsCreated
        };
    }

    // ═══════════════════════════════════════════════════════════════
    // 🎯 PROCESSAR WEBHOOK DO N8N (FORMATO CUSTOMIZADO)
    // ═══════════════════════════════════════════════════════════════

    public async Task<N8nProcessResult> ProcessN8nWebhookAsync(N8nWebhookDto webhook)
    {
        var phone = NormalizePhone(webhook.Phone);

        // 1️⃣ Salva webhook bruto
        var webhookEvent = new WebhookEvent
        {
            Provider = "n8n",
            EventType = "whatsapp_message",
            PayloadJson = JsonSerializer.Serialize(webhook),
            PhoneNumberId = phone,
            TenantId = webhook.TenantId,
            ReceivedAt = DateTime.UtcNow
        };

        _db.WebhookEvents.Add(webhookEvent);
        await _db.SaveChangesAsync();

        // 2️⃣ Cria OriginEvent
        var sourceType = DetermineSourceType(webhook.CtwaClid, webhook.SourceType);
        var confidence = DetermineConfidence(sourceType, webhook.CtwaClid);

        var originEvent = new OriginEvent
        {
            Phone = phone,
            ContactName = webhook.ContactName,
            CtwaClid = webhook.CtwaClid ?? "UNKNOWN",
            SourceId = webhook.SourceId,
            SourceType = sourceType,
            SourceUrl = webhook.SourceUrl,
            Headline = webhook.Headline,
            Body = webhook.Body,
            MessageId = webhook.MessageId,
            MessageTimestamp = ParseTimestamp(webhook.MessageTimestamp),
            WebhookEventId = webhookEvent.Id,
            TenantId = webhook.TenantId,
            Confidence = confidence,
            Processed = false,
            ReceivedAt = DateTime.UtcNow
        };

        _db.OriginEvents.Add(originEvent);
        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "✅ OriginEvent criado via n8n: Phone {Phone} → {SourceType} ({Confidence})",
            phone, sourceType, confidence);

        return new N8nProcessResult
        {
            OriginEventId = originEvent.Id,
            Phone = phone,
            SourceType = sourceType
        };
    }

    // ═══════════════════════════════════════════════════════════════
    // 🔧 MÉTODOS AUXILIARES
    // ═══════════════════════════════════════════════════════════════

    private async Task<WebhookEvent> SaveWebhookEventAsync(MetaWebhookDto webhook, string phone)
    {
        var webhookEvent = new WebhookEvent
        {
            Provider = "meta",
            EventType = "whatsapp_message",
            PayloadJson = JsonSerializer.Serialize(webhook),
            PhoneNumberId = phone,
            ReceivedAt = DateTime.UtcNow
        };

        _db.WebhookEvents.Add(webhookEvent);
        await _db.SaveChangesAsync();

        return webhookEvent;
    }

    public async Task<OriginEvent> CreateOriginEventFromReferralAsync(
        MetaReferralDto referral,
        string phone,
        string? contactName,
        int webhookEventId)
    {
        var sourceType = DetermineSourceType(referral.CtwaClid, referral.SourceType);
        var confidence = DetermineConfidence(sourceType, referral.CtwaClid);

        var originEvent = new OriginEvent
        {
            Phone = phone,
            ContactName = contactName,
            CtwaClid = referral.CtwaClid ?? "UNKNOWN",
            SourceId = referral.SourceId,
            SourceType = sourceType,
            SourceUrl = referral.SourceUrl,
            Headline = referral.Headline,
            Body = referral.Body,
            WebhookEventId = webhookEventId,
            Confidence = confidence,
            Processed = false,
            ReceivedAt = DateTime.UtcNow
        };

        _db.OriginEvents.Add(originEvent);
        await _db.SaveChangesAsync();

        return originEvent;
    }

    private async Task<OriginEvent> CreateSocialOriginEventAsync(
        string phone,
        string? contactName,
        MetaMessageDto message,
        int webhookEventId)
    {
        var originEvent = new OriginEvent
        {
            Phone = phone,
            ContactName = contactName,
            CtwaClid = "SOCIAL_" + Guid.NewGuid().ToString("N")[..8],
            SourceType = "SOCIAL",
            MessageId = message.Id,
            MessageTimestamp = ParseTimestamp(message.Timestamp),
            WebhookEventId = webhookEventId,
            Confidence = "MEDIUM",
            Processed = false,
            ReceivedAt = DateTime.UtcNow
        };

        _db.OriginEvents.Add(originEvent);
        await _db.SaveChangesAsync();

        return originEvent;
    }

    /// <summary>
    /// Determina o tipo de origem baseado nos dados disponíveis
    /// Prioridade: CtwaClid (AD) > SourceType > UNKNOWN
    /// </summary>
    private static string DetermineSourceType(string? ctwaClid, string? sourceType)
    {
        // Se tem CtwaClid = veio de anúncio
        if (!string.IsNullOrWhiteSpace(ctwaClid) && ctwaClid != "UNKNOWN")
        {
            return "AD";
        }

        // Se tem SourceType explícito
        if (!string.IsNullOrWhiteSpace(sourceType))
        {
            return sourceType.ToUpperInvariant() switch
            {
                "AD" or "ADS" or "ADVERTISEMENT" => "AD",
                "POST" or "SOCIAL" or "ORGANIC" => "SOCIAL",
                _ => "UNKNOWN"
            };
        }

        return "UNKNOWN";
    }

    /// <summary>
    /// Determina o nível de confiança da atribuição
    /// </summary>
    private static string DetermineConfidence(string sourceType, string? ctwaClid)
    {
        return sourceType switch
        {
            "AD" when !string.IsNullOrWhiteSpace(ctwaClid) => "HIGH",
            "AD" => "MEDIUM",
            "SOCIAL" => "MEDIUM",
            _ => "LOW"
        };
    }

    /// <summary>
    /// Normaliza telefone: remove tudo que não é dígito
    /// </summary>
    private static string NormalizePhone(string phone)
    {
        return new string(phone.Where(char.IsDigit).ToArray());
    }

    /// <summary>
    /// Converte timestamp Unix para DateTime
    /// </summary>
    private static DateTime? ParseTimestamp(string? timestamp)
    {
        if (string.IsNullOrWhiteSpace(timestamp))
            return null;

        if (long.TryParse(timestamp, out var unixTimestamp))
        {
            return DateTimeOffset.FromUnixTimeSeconds(unixTimestamp).UtcDateTime;
        }

        if (DateTime.TryParse(timestamp, out var dateTime))
        {
            return dateTime.ToUniversalTime();
        }

        return null;
    }
}

// ═══════════════════════════════════════════════════════════════
// 📦 RESULT DTOs
// ═══════════════════════════════════════════════════════════════

public class WebhookProcessResult
{
    public int EventsProcessed { get; set; }
    public int OriginEventsCreated { get; set; }
}

public class N8nProcessResult
{
    public int OriginEventId { get; set; }
    public string Phone { get; set; } = null!;
    public string SourceType { get; set; } = null!;
}