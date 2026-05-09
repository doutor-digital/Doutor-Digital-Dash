using System.Text.Json;
using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.DTOs.Cloudia;
using LeadAnalytics.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace LeadAnalytics.Api.Service.Stages;

/// <summary>
/// Recebe um <see cref="WebhookEnvelope"/> já enfileirado e despacha pra ação
/// correta de acordo com a etapa nova (StageTo).
///
/// Cada handler é IDEMPOTENTE: se o registro já existe e já está no estado
/// correto, não cria nada novo. Isso protege contra reprocessamento (retry)
/// do worker.
/// </summary>
public class StageWebhookDispatcher
{
    private readonly AppDbContext _db;
    private readonly LeadService _leadService;
    private readonly ILogger<StageWebhookDispatcher> _logger;

    public StageWebhookDispatcher(
        AppDbContext db,
        LeadService leadService,
        ILogger<StageWebhookDispatcher> logger)
    {
        _db = db;
        _leadService = leadService;
        _logger = logger;
    }

    public async Task DispatchAsync(WebhookEnvelope env, CancellationToken ct = default)
    {
        var dto = JsonSerializer.Deserialize<CloudiaWebhookDto>(env.Payload);
        var data = dto?.Data ?? dto?.Customer
            ?? throw new InvalidOperationException("payload sem 'data'");

        var stage = CloudiaStages.Normalize(env.StageTo);

        _logger.LogInformation(
            "🎯 Dispatch envelope #{Id} stage={Stage} contact={Contact}",
            env.Id, stage, env.ContactId);

        // 1) Sempre garante o Lead existe / atualizado (entrada padrão).
        var lead = await EnsureLeadAsync(dto!, ct);

        // 2) Sempre registra a transição de etapa (se mudou).
        await EnsureStageHistoryAsync(lead, stage, env.OccurredAt, ct);

        // 3) Handler específico por etapa.
        switch (stage)
        {
            case CloudiaStages.EntradaLead:
                // Já tratado por EnsureLeadAsync — nada extra.
                break;

            case CloudiaStages.AgendadoSemPagamento:
                await UpsertConsultationAsync(lead, paidInAdvance: false, env.OccurredAt, ct);
                break;

            case CloudiaStages.AgendadoComPagamento:
                await UpsertConsultationAsync(lead, paidInAdvance: true, env.OccurredAt, ct);
                break;

            case CloudiaStages.NaoCompareceu:
                await MarkConsultationAsync(lead, attended: false, status: "faltou", env.OccurredAt, ct);
                break;

            case CloudiaStages.CompareceuConsulta:
                await MarkConsultationAsync(lead, attended: true, status: "realizada", env.OccurredAt, ct);
                break;

            case CloudiaStages.TratamentoFechado:
                await CreateTreatmentAwaitingDataAsync(lead, env.OccurredAt, ct);
                break;

            case CloudiaStages.NaoDeuContinuidade:
                await MarkTreatmentLostAsync(lead, env.OccurredAt, ct);
                break;

            default:
                // Etapas neutras (02, 03, 12, 13, 14, 15, 16) só atualizam o Lead.
                break;
        }

        await _db.SaveChangesAsync(ct);
    }

    // ─── Lead ────────────────────────────────────────────────────────────

    private async Task<Lead> EnsureLeadAsync(CloudiaWebhookDto dto, CancellationToken ct)
    {
        var data = dto.Data ?? dto.Customer!;
        var lead = await _db.Leads
            .FirstOrDefaultAsync(l => l.ExternalId == data.Id && l.TenantId == data.ClinicId, ct);

        if (lead == null)
        {
            // Reaproveita o pipeline existente que normaliza phone/source/etc.
            // SaveLeadAsync grava o lead — depois trazemos do banco pra termos a ref.
            await _leadService.SaveLeadAsync(dto);
            lead = await _db.Leads
                .FirstOrDefaultAsync(l => l.ExternalId == data.Id && l.TenantId == data.ClinicId, ct);

            if (lead == null)
                throw new InvalidOperationException($"Falha ao criar Lead {data.Id}/{data.ClinicId}");
        }
        else
        {
            // Atualiza campos voláteis (etapa, tags, observações).
            if (!string.IsNullOrWhiteSpace(data.Stage)) lead.CurrentStage = data.Stage!;
            if (data.IdStage.HasValue) lead.CurrentStageId = data.IdStage.Value;
            if (!string.IsNullOrWhiteSpace(data.Observations)) lead.Observations = data.Observations;
            if (data.HasHealthInsurancePlan.HasValue)
                lead.HasHealthInsurancePlan = data.HasHealthInsurancePlan;
            lead.UpdatedAt = DateTime.UtcNow;
        }

        return lead;
    }

    // ─── Stage history ───────────────────────────────────────────────────

    private async Task EnsureStageHistoryAsync(Lead lead, string stage, DateTime occurredAt, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(stage)) return;

        var alreadyAtThisStage = await _db.LeadStageHistories
            .AnyAsync(h => h.LeadId == lead.Id
                        && h.StageLabel == stage
                        && h.ChangedAt == occurredAt, ct);

        if (alreadyAtThisStage) return;

        _db.LeadStageHistories.Add(new LeadStageHistory
        {
            LeadId = lead.Id,
            StageId = lead.CurrentStageId ?? 0,
            StageLabel = stage,
            ChangedAt = occurredAt,
        });

        lead.CurrentStage = stage;
        lead.UpdatedAt = DateTime.UtcNow;
    }

    // ─── Consultation ────────────────────────────────────────────────────

    private async Task UpsertConsultationAsync(Lead lead, bool paidInAdvance, DateTime occurredAt, CancellationToken ct)
    {
        // Idempotência: se já existe Consulta agendada para esse lead, atualiza.
        var existing = await _db.Consultations
            .Where(c => c.LeadId == lead.Id
                     && (c.Status == "agendada" || c.Status == "realizada"))
            .OrderByDescending(c => c.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (existing != null)
        {
            existing.PaidInAdvance = paidInAdvance;
            existing.ScheduledAt ??= occurredAt;
            existing.UpdatedAt = DateTime.UtcNow;
            return;
        }

        _db.Consultations.Add(new Consultation
        {
            LeadId = lead.Id,
            TenantId = lead.TenantId,
            UnitId = lead.UnitId,
            ScheduledAt = occurredAt,
            PaidInAdvance = paidInAdvance,
            Status = "agendada",
        });
    }

    private async Task MarkConsultationAsync(Lead lead, bool attended, string status, DateTime occurredAt, CancellationToken ct)
    {
        var consultation = await _db.Consultations
            .Where(c => c.LeadId == lead.Id)
            .OrderByDescending(c => c.ScheduledAt ?? c.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (consultation == null)
        {
            // Cliente compareceu sem ter passado pelas etapas 04/05? Cria a consulta retroativa.
            consultation = new Consultation
            {
                LeadId = lead.Id,
                TenantId = lead.TenantId,
                UnitId = lead.UnitId,
                ScheduledAt = occurredAt,
                Status = status,
                Attended = attended,
                AttendedAt = attended ? occurredAt : null,
            };
            _db.Consultations.Add(consultation);
            return;
        }

        consultation.Attended = attended;
        consultation.AttendedAt = attended ? occurredAt : null;
        consultation.Status = status;
        consultation.UpdatedAt = DateTime.UtcNow;
    }

    // ─── Treatment ───────────────────────────────────────────────────────

    private async Task CreateTreatmentAwaitingDataAsync(Lead lead, DateTime occurredAt, CancellationToken ct)
    {
        // Idempotência: se já existe Treatment ativo (não cancelado/perdido) pra esse lead, não cria outro.
        var alreadyOpen = await _db.Treatments
            .AnyAsync(t => t.LeadId == lead.Id
                        && t.Status != "cancelado"
                        && !t.ClosedAsLost, ct);

        if (alreadyOpen)
        {
            _logger.LogInformation(
                "Lead {LeadId} já tem Treatment aberto — não criamos outro.", lead.Id);
            return;
        }

        var lastConsultation = await _db.Consultations
            .Where(c => c.LeadId == lead.Id)
            .OrderByDescending(c => c.ScheduledAt ?? c.CreatedAt)
            .Select(c => (int?)c.Id)
            .FirstOrDefaultAsync(ct);

        _db.Treatments.Add(new Treatment
        {
            LeadId = lead.Id,
            ConsultationId = lastConsultation,
            TenantId = lead.TenantId,
            UnitId = lead.UnitId,
            Status = "aguardando_dados",
            CreatedAt = occurredAt,
            UpdatedAt = DateTime.UtcNow,
        });
    }

    private async Task MarkTreatmentLostAsync(Lead lead, DateTime occurredAt, CancellationToken ct)
    {
        var open = await _db.Treatments
            .Where(t => t.LeadId == lead.Id
                     && t.Status != "cancelado"
                     && !t.ClosedAsLost)
            .OrderByDescending(t => t.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (open == null)
        {
            // Sem Treatment prévio — cria registro só pra capturar o evento.
            open = new Treatment
            {
                LeadId = lead.Id,
                TenantId = lead.TenantId,
                UnitId = lead.UnitId,
                Status = "cancelado",
                CreatedAt = occurredAt,
            };
            _db.Treatments.Add(open);
        }

        open.ClosedAsLost = true;
        open.Status = "cancelado";
        open.UpdatedAt = DateTime.UtcNow;
        // RejectionReason fica null aqui — a SDR preenche via UI depois.
    }
}
