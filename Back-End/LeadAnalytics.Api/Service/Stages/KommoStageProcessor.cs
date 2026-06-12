using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace LeadAnalytics.Api.Service.Stages;

/// <summary>
/// Reage a uma transição de etapa canônica de um lead, criando/atualizando
/// Consulta e Tratamento — a automação que antes vivia no StageWebhookDispatcher
/// (Cloudia), agora dirigida pelos eventos da Kommo.
///
/// Cada handler é IDEMPOTENTE: reprocessar o mesmo evento não duplica registros.
/// O chamador deve dar SaveChangesAsync depois (ou confiar no SaveChanges do
/// próprio _db, que é compartilhado por request).
/// </summary>
public class KommoStageProcessor(AppDbContext db, ILogger<KommoStageProcessor> logger)
{
    private readonly AppDbContext _db = db;
    private readonly ILogger<KommoStageProcessor> _logger = logger;

    /// <summary>
    /// Aplica a etapa canônica ao lead. Retorna true se executou algum handler
    /// específico (consulta/tratamento). Stages desconhecidas só atualizam o Lead.
    /// </summary>
    public async Task ApplyAsync(Lead lead, string canonicalStage, DateTime occurredAt, CancellationToken ct = default)
    {
        var stage = CanonicalStages.Normalize(canonicalStage);

        switch (stage)
        {
            case CanonicalStages.AgendadoSemPagamento:
                await UpsertConsultationAsync(lead, paidInAdvance: false, occurredAt, ct);
                break;

            case CanonicalStages.AgendadoComPagamento:
                await UpsertConsultationAsync(lead, paidInAdvance: true, occurredAt, ct);
                break;

            case CanonicalStages.NaoCompareceu:
                await MarkConsultationAsync(lead, attended: false, status: "faltou", occurredAt, ct);
                break;

            case CanonicalStages.CompareceuConsulta:
                await MarkConsultationAsync(lead, attended: true, status: "realizada", occurredAt, ct);
                break;

            case CanonicalStages.TratamentoFechado:
                await CreateTreatmentAwaitingDataAsync(lead, occurredAt, ct);
                break;

            case CanonicalStages.NaoDeuContinuidade:
                await MarkTreatmentLostAsync(lead, occurredAt, ct);
                break;

            default:
                // ENTRADA_LEAD e etapas neutras: nada além de atualizar o Lead (feito pelo caller).
                break;
        }
    }

    // ─── Consultation ────────────────────────────────────────────────────

    private async Task UpsertConsultationAsync(Lead lead, bool paidInAdvance, DateTime occurredAt, CancellationToken ct)
    {
        // A etapa "Agendado com pagamento" (05_*) é por definição um lead que JÁ pagou
        // a consulta antecipadamente. Sem isso, o card "Agendados" mostrava "Sem pagamento
        // antecipado" pra esses leads (HasPayment ficava false porque só PaymentService o
        // setava — flow de boleto/PIX manual). Nunca DESmarcamos aqui: voltar pra 04 não
        // estorna um pagamento que já existiu.
        if (paidInAdvance && !lead.HasPayment) lead.HasPayment = true;

        var existing = await _db.Consultations
            .Where(c => c.LeadId == lead.Id && (c.Status == "agendada" || c.Status == "realizada"))
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
            _db.Consultations.Add(new Consultation
            {
                LeadId = lead.Id,
                TenantId = lead.TenantId,
                UnitId = lead.UnitId,
                ScheduledAt = occurredAt,
                Status = status,
                Attended = attended,
                AttendedAt = attended ? occurredAt : null,
            });
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
        var alreadyOpen = await _db.Treatments
            .AnyAsync(t => t.LeadId == lead.Id && t.Status != "cancelado" && !t.ClosedAsLost, ct);

        if (alreadyOpen)
        {
            _logger.LogInformation("Lead {LeadId} já tem Treatment aberto — não criamos outro.", lead.Id);
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
            .Where(t => t.LeadId == lead.Id && t.Status != "cancelado" && !t.ClosedAsLost)
            .OrderByDescending(t => t.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (open == null)
        {
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
    }
}
