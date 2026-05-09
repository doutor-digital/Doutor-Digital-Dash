using System.Text.Json;
using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.DTOs.Cloudia;
using LeadAnalytics.Api.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LeadAnalytics.Api.Service;

/// <summary>
/// Recebe um payload do webhook Cloudia e enfileira em <see cref="WebhookEnvelope"/>.
/// Idempotência via índice único (provider, contact_id, stage_to, occurred_at).
///
/// Resposta retorna rápido (200 OK) — o processamento real é feito por
/// <see cref="WebhookProcessorJob"/> em background.
/// </summary>
public class WebhookEnqueueService
{
    private readonly AppDbContext _db;
    private readonly ILogger<WebhookEnqueueService> _logger;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = false };

    public WebhookEnqueueService(AppDbContext db, ILogger<WebhookEnqueueService> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Enfileira o webhook. Retorna o envelope salvo, ou null se já existia
    /// (deduplicado pela chave de idempotência).
    /// </summary>
    public async Task<EnqueueResult> EnqueueCloudiaAsync(
        CloudiaWebhookDto dto,
        CancellationToken ct = default)
    {
        var data = dto.Data ?? dto.Customer;
        if (data is null)
            return new EnqueueResult(EnqueueStatus.Invalid, null, "payload sem 'data' nem 'customer'");

        if (data.Id == 0)
            return new EnqueueResult(EnqueueStatus.Invalid, null, "data.id ausente");

        var stage = (data.Stage ?? string.Empty).Trim();
        // Se não veio etapa, usamos o type pra ter pelo menos chave única.
        if (string.IsNullOrWhiteSpace(stage))
            stage = $"event:{dto.Type ?? "unknown"}";

        var occurred = data.LastUpdatedAt ?? data.CreatedAt ?? DateTime.UtcNow;
        // Normaliza precisão (Postgres = microssegundos; payload pode trazer ms)
        occurred = DateTime.SpecifyKind(occurred, DateTimeKind.Utc);

        var envelope = new WebhookEnvelope
        {
            Provider = "cloudia",
            ContactId = data.Id.ToString(),
            TenantId = data.ClinicId == 0 ? null : data.ClinicId,
            StageTo = stage,
            OccurredAt = occurred,
            ReceivedAt = DateTime.UtcNow,
            Status = "pending",
            Attempts = 0,
            NextAttemptAt = DateTime.UtcNow,
            Payload = JsonSerializer.Serialize(dto, JsonOpts),
        };

        try
        {
            _db.WebhookEnvelopes.Add(envelope);
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation(
                "📥 Webhook enfileirado #{Id}: {Provider}/{Contact} → {Stage} @ {When:o}",
                envelope.Id, envelope.Provider, envelope.ContactId, envelope.StageTo, envelope.OccurredAt);
            return new EnqueueResult(EnqueueStatus.Enqueued, envelope.Id, null);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            _db.Entry(envelope).State = EntityState.Detached;
            _logger.LogInformation(
                "🔁 Webhook duplicado ignorado: {Provider}/{Contact} → {Stage} @ {When:o}",
                envelope.Provider, envelope.ContactId, envelope.StageTo, envelope.OccurredAt);
            return new EnqueueResult(EnqueueStatus.Duplicate, null, null);
        }
    }

    private static bool IsUniqueViolation(DbUpdateException ex)
    {
        // Postgres SQLSTATE 23505 = unique_violation
        return ex.InnerException is PostgresException pg && pg.SqlState == "23505";
    }
}

public enum EnqueueStatus
{
    Enqueued,
    Duplicate,
    Invalid,
}

public record EnqueueResult(EnqueueStatus Status, long? EnvelopeId, string? Reason);
