using System.Text.Json;
using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.DTOs.Sdr;
using LeadAnalytics.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace LeadAnalytics.Api.Service;

public class SdrLeadService(
    AppDbContext db,
    SdrAuditLogService audit,
    ILogger<SdrLeadService> logger)
{
    private readonly AppDbContext _db = db;
    private readonly SdrAuditLogService _audit = audit;
    private readonly ILogger<SdrLeadService> _logger = logger;

    private static readonly JsonSerializerOptions JsonOpts = new();

    // ─── List / Get ──────────────────────────────────────────────

    public async Task<List<SdrLeadResponseDto>> ListAsync(
        int tenantId,
        string? status = null,
        string? source = null,
        string? search = null,
        int? unitId = null,
        int page = 1,
        int pageSize = 100,
        CancellationToken ct = default)
    {
        var query = _db.SdrLeads
            .AsNoTracking()
            .Include(l => l.ReviewedByUser)
            .Where(l => l.TenantId == tenantId);

        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(l => l.Status == status);
        if (!string.IsNullOrWhiteSpace(source)) query = query.Where(l => l.Source == source);
        if (unitId.HasValue) query = query.Where(l => l.UnitId == unitId);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            query = query.Where(l =>
                l.Nome.ToLower().Contains(s) ||
                l.Telefone.ToLower().Contains(s) ||
                (l.Observacao != null && l.Observacao.ToLower().Contains(s)));
        }

        if (page < 1) page = 1;
        if (pageSize < 1 || pageSize > 500) pageSize = 100;

        var leads = await query
            .OrderByDescending(l => l.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return leads.Select(MapToDto).ToList();
    }

    public async Task<SdrLeadResponseDto?> GetByIdAsync(int tenantId, int id, CancellationToken ct = default)
    {
        var lead = await _db.SdrLeads
            .AsNoTracking()
            .Include(l => l.ReviewedByUser)
            .FirstOrDefaultAsync(l => l.Id == id && l.TenantId == tenantId, ct);
        return lead is null ? null : MapToDto(lead);
    }

    public async Task<SdrLeadResponseDto> CreateManualAsync(
        int tenantId, SdrLeadCreateDto dto, CancellationToken ct = default)
    {
        Validate(dto);

        var now = DateTime.UtcNow;
        var lead = new SdrLead
        {
            TenantId = tenantId,
            Nome = dto.Nome.Trim(),
            Telefone = dto.Telefone.Trim(),
            Tipo = dto.Tipo,
            Origem = dto.Origem,
            TipoResgate = dto.TipoResgate,
            Interacao = dto.Interacao,
            AgendouConsulta = dto.AgendouConsulta,
            DataAgendamento = ToUtc(dto.DataAgendamento),
            MotivoNaoAgendamento = dto.MotivoNaoAgendamento,
            NomeResponsavel = dto.NomeResponsavel.Trim(),
            Login = dto.Login,
            Observacao = dto.Observacao,
            Situacao = dto.Situacao,
            Clinica = dto.Clinica,
            UnitId = dto.UnitId,
            AttendantId = dto.AttendantId,
            ImportBatchId = dto.ImportBatchId,
            Source = NormalizeSource(dto.Source),
            // Manual e importado já entram aprovados (sem revisão)
            Status = NormalizeSource(dto.Source) == "cloudia" ? "pendente_revisao" : "aprovado",
            DataOrigem = now,
            DataModificacao = now,
            CreatedAt = now,
            UpdatedAt = now,
        };

        _db.SdrLeads.Add(lead);
        await _db.SaveChangesAsync(ct);

        await _audit.RecordAsync(
            tenantId,
            action: $"sdr_lead.created_{lead.Source}",
            entityType: "SdrLead",
            entityId: lead.Id,
            summary: $"Criou lead {lead.Nome} ({lead.Source})",
            after: MapToDto(lead),
            ct: ct);

        return MapToDto(lead);
    }

    public async Task<SdrLeadResponseDto?> UpdateAsync(
        int tenantId, int id, SdrLeadUpdateDto dto, CancellationToken ct = default)
    {
        Validate(dto);

        var lead = await _db.SdrLeads.FirstOrDefaultAsync(
            l => l.Id == id && l.TenantId == tenantId, ct);
        if (lead is null) return null;

        var before = MapToDto(lead);

        lead.Nome = dto.Nome.Trim();
        lead.Telefone = dto.Telefone.Trim();
        lead.Tipo = dto.Tipo;
        lead.Origem = dto.Origem;
        lead.TipoResgate = dto.TipoResgate;
        lead.Interacao = dto.Interacao;
        lead.AgendouConsulta = dto.AgendouConsulta;
        lead.DataAgendamento = ToUtc(dto.DataAgendamento);
        lead.MotivoNaoAgendamento = dto.MotivoNaoAgendamento;
        lead.NomeResponsavel = dto.NomeResponsavel.Trim();
        lead.Login = dto.Login;
        lead.Observacao = dto.Observacao;
        lead.Situacao = dto.Situacao;
        lead.Clinica = dto.Clinica;
        lead.UnitId = dto.UnitId;
        lead.AttendantId = dto.AttendantId;
        lead.ImportBatchId = dto.ImportBatchId;

        if (dto.CloudiaFields is not null)
            lead.CloudiaFields = JsonSerializer.Serialize(dto.CloudiaFields);

        lead.UpdatedAt = DateTime.UtcNow;
        lead.DataModificacao = lead.UpdatedAt;

        await _db.SaveChangesAsync(ct);

        var after = MapToDto(lead);
        await _audit.RecordAsync(
            tenantId,
            action: "sdr_lead.updated",
            entityType: "SdrLead",
            entityId: lead.Id,
            summary: $"Editou lead {lead.Nome}",
            before: before,
            after: after,
            ct: ct);

        return after;
    }

    /// <summary>
    /// Promove um lead da fase "pendente_revisao" para "aprovado".
    /// É O ponto crítico do fluxo CRM: depois daqui o lead aparece em /sdr/leads-aprovados.
    /// </summary>
    public async Task<SdrLeadResponseDto?> ReviewAsync(
        int tenantId, int id, SdrLeadReviewActionDto dto, CancellationToken ct = default)
    {
        var lead = await _db.SdrLeads.FirstOrDefaultAsync(
            l => l.Id == id && l.TenantId == tenantId, ct);
        if (lead is null) return null;

        if (lead.Status == "aprovado" && dto.Action == "approve")
            throw new InvalidOperationException("Lead já está aprovado");

        var before = MapToDto(lead);

        // Aplicar patch (campos editados durante a revisão), se vier
        if (dto.Patch is not null)
        {
            var patch = dto.Patch;
            Validate(patch);
            lead.Nome = patch.Nome.Trim();
            lead.Telefone = patch.Telefone.Trim();
            lead.Tipo = patch.Tipo;
            lead.Origem = patch.Origem;
            lead.TipoResgate = patch.TipoResgate;
            lead.Interacao = patch.Interacao;
            lead.AgendouConsulta = patch.AgendouConsulta;
            lead.DataAgendamento = ToUtc(patch.DataAgendamento);
            lead.MotivoNaoAgendamento = patch.MotivoNaoAgendamento;
            lead.NomeResponsavel = patch.NomeResponsavel.Trim();
            lead.Login = patch.Login;
            lead.Observacao = patch.Observacao;
            lead.Situacao = patch.Situacao;
            lead.Clinica = patch.Clinica;
            if (patch.CloudiaFields is not null)
                lead.CloudiaFields = JsonSerializer.Serialize(patch.CloudiaFields);
        }

        var now = DateTime.UtcNow;
        var action = dto.Action?.ToLowerInvariant() ?? "approve";

        switch (action)
        {
            case "approve":
                lead.Status = "aprovado";
                lead.ReviewedAt = now;
                lead.RejectionReason = null;
                break;
            case "reject":
                lead.Status = "rejeitado";
                lead.ReviewedAt = now;
                lead.RejectionReason = dto.RejectionReason;
                break;
            default:
                throw new ArgumentException($"Ação inválida: '{dto.Action}'. Use 'approve' ou 'reject'.");
        }

        lead.UpdatedAt = now;
        lead.DataModificacao = now;

        await _db.SaveChangesAsync(ct);

        var after = MapToDto(lead);
        await _audit.RecordAsync(
            tenantId,
            action: action == "approve" ? "sdr_lead.review_approved" : "sdr_lead.review_rejected",
            entityType: "SdrLead",
            entityId: lead.Id,
            summary: action == "approve"
                ? $"Aprovou revisão de {lead.Nome}"
                : $"Rejeitou revisão de {lead.Nome}: {dto.RejectionReason ?? "sem motivo"}",
            before: before,
            after: after,
            ct: ct);

        _logger.LogInformation("✅ SDR lead {Id} {Action} (tenant={Tenant})", lead.Id, action, tenantId);
        return after;
    }

    public async Task<bool> DeleteAsync(int tenantId, int id, CancellationToken ct = default)
    {
        var lead = await _db.SdrLeads.FirstOrDefaultAsync(
            l => l.Id == id && l.TenantId == tenantId, ct);
        if (lead is null) return false;

        var before = MapToDto(lead);
        _db.SdrLeads.Remove(lead);
        await _db.SaveChangesAsync(ct);

        await _audit.RecordAsync(
            tenantId,
            action: "sdr_lead.deleted",
            entityType: "SdrLead",
            entityId: id,
            summary: $"Removeu lead {before.Nome}",
            before: before,
            ct: ct);

        return true;
    }

    /// <summary>
    /// Persistência de lead vindo do webhook Cloudia. Não chama auditoria humana
    /// (UserId=null no audit log → ação automática). Status="pendente_revisao".
    /// </summary>
    public async Task<SdrLead> UpsertFromCloudiaAsync(
        int tenantId,
        int externalId,
        string nome,
        string telefone,
        string origem,
        string tipo,
        string? tipoResgate,
        bool interacao,
        bool agendouConsulta,
        string nomeResponsavel,
        string? login,
        string? observacao,
        string? situacao,
        string? clinica,
        DateTime dataOrigem,
        DateTime? dataModificacao,
        List<string> cloudiaFields,
        string? webhookEvent,
        CancellationToken ct = default)
    {
        var existing = await _db.SdrLeads.FirstOrDefaultAsync(
            l => l.TenantId == tenantId && l.ExternalId == externalId, ct);

        var now = DateTime.UtcNow;
        var fieldsJson = JsonSerializer.Serialize(cloudiaFields);

        if (existing is null)
        {
            var lead = new SdrLead
            {
                TenantId = tenantId,
                ExternalId = externalId,
                Nome = nome,
                Telefone = telefone,
                Tipo = tipo,
                Origem = origem,
                TipoResgate = tipoResgate,
                Interacao = interacao,
                AgendouConsulta = agendouConsulta,
                NomeResponsavel = nomeResponsavel,
                Login = login,
                Observacao = observacao,
                Situacao = situacao,
                Clinica = clinica,
                DataOrigem = dataOrigem,
                DataModificacao = dataModificacao ?? dataOrigem,
                Source = "cloudia",
                Status = "pendente_revisao",
                CloudiaFields = fieldsJson,
                CloudiaReceivedAt = now,
                CloudiaWebhookEvent = webhookEvent,
                CreatedAt = now,
                UpdatedAt = now,
            };
            _db.SdrLeads.Add(lead);
            await _db.SaveChangesAsync(ct);

            await _audit.RecordAsync(
                tenantId,
                action: "sdr_lead.cloudia_received",
                entityType: "SdrLead",
                entityId: lead.Id,
                summary: $"Webhook Cloudia criou lead {lead.Nome}",
                after: MapToDto(lead),
                ct: ct);

            return lead;
        }

        // Update — só sobrescreve se ainda está pendente. Aprovados são imutáveis pelo webhook.
        if (existing.Status == "pendente_revisao")
        {
            existing.Nome = nome;
            existing.Telefone = telefone;
            existing.Origem = origem;
            existing.Tipo = tipo;
            existing.TipoResgate = tipoResgate;
            existing.Interacao = interacao;
            existing.AgendouConsulta = agendouConsulta;
            existing.NomeResponsavel = nomeResponsavel;
            existing.Login = login;
            existing.Observacao = observacao;
            existing.Situacao = situacao;
            existing.Clinica = clinica;
            existing.DataModificacao = dataModificacao ?? now;
            existing.CloudiaFields = fieldsJson;
            existing.CloudiaWebhookEvent = webhookEvent;
            existing.UpdatedAt = now;
            await _db.SaveChangesAsync(ct);

            await _audit.RecordAsync(
                tenantId,
                action: "sdr_lead.cloudia_updated",
                entityType: "SdrLead",
                entityId: existing.Id,
                summary: $"Webhook Cloudia atualizou lead {existing.Nome}",
                after: MapToDto(existing),
                ct: ct);
        }

        return existing;
    }

    // ─── Helpers ─────────────────────────────────────────────────

    private static void Validate(SdrLeadCreateDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Nome)) throw new ArgumentException("Nome obrigatório");
        if (string.IsNullOrWhiteSpace(dto.Telefone)) throw new ArgumentException("Telefone obrigatório");
        if (string.IsNullOrWhiteSpace(dto.NomeResponsavel)) throw new ArgumentException("Nome do responsável obrigatório");
        if (dto.Tipo != "Cadastro" && dto.Tipo != "Resgate")
            throw new ArgumentException($"Tipo inválido: '{dto.Tipo}'. Use 'Cadastro' ou 'Resgate'.");
    }

    private static string NormalizeSource(string? s) => s switch
    {
        "cloudia" or "manual" or "importado" => s,
        null or "" => "manual",
        _ => "manual",
    };

    private static DateTime? ToUtc(DateTime? d) =>
        d?.Kind switch
        {
            DateTimeKind.Utc => d,
            DateTimeKind.Local => d.Value.ToUniversalTime(),
            DateTimeKind.Unspecified => DateTime.SpecifyKind(d.Value, DateTimeKind.Utc),
            _ => d,
        };

    public static SdrLeadResponseDto MapToDto(SdrLead l)
    {
        var fields = ParseFields(l.CloudiaFields);
        return new SdrLeadResponseDto
        {
            Id = l.Id,
            TenantId = l.TenantId,
            ExternalId = l.ExternalId,
            Nome = l.Nome,
            Telefone = l.Telefone,
            Tipo = l.Tipo,
            Origem = l.Origem,
            TipoResgate = l.TipoResgate,
            Interacao = l.Interacao,
            AgendouConsulta = l.AgendouConsulta,
            DataAgendamento = l.DataAgendamento,
            MotivoNaoAgendamento = l.MotivoNaoAgendamento,
            NomeResponsavel = l.NomeResponsavel,
            Login = l.Login,
            Observacao = l.Observacao,
            Situacao = l.Situacao,
            Clinica = l.Clinica,
            DataOrigem = l.DataOrigem,
            DataModificacao = l.DataModificacao,
            Source = l.Source,
            Status = l.Status,
            ReviewedAt = l.ReviewedAt,
            ReviewedByUserId = l.ReviewedByUserId,
            ReviewedByName = l.ReviewedByUser?.Name,
            RejectionReason = l.RejectionReason,
            CloudiaFields = fields,
            CloudiaReceivedAt = l.CloudiaReceivedAt,
            CloudiaWebhookEvent = l.CloudiaWebhookEvent,
            UnitId = l.UnitId,
            AttendantId = l.AttendantId,
            ImportBatchId = l.ImportBatchId,
            CreatedAt = l.CreatedAt,
            UpdatedAt = l.UpdatedAt,
        };
    }

    private static List<string> ParseFields(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json, JsonOpts) ?? [];
        }
        catch
        {
            return [];
        }
    }
}
