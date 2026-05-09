using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.DTOs.Sdr;
using LeadAnalytics.Api.Service;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LeadAnalytics.Api.Controllers;

/// <summary>
/// Endpoints para a tela "Revisar leads" do SDR.
///
/// Modelo de dados: a Cloudia empurra leads via POST /webhooks/cloudia →
/// LeadService.SaveLeadAsync persiste na tabela `leads`. Esta controller
/// expõe esses leads no formato esperado pelo front (SdrLeadResponseDto)
/// pra mergeagem com o store local de revisão.
///
/// IMPORTANTE: não há tabela `sdr_leads` separada — a "fila de revisão"
/// vive no localStorage do front (zustand). A SyncSummary devolve sempre
/// o snapshot atual do tenant; o front faz dedupe por Id.
/// </summary>
[ApiController]
[Route("api/sdr/leads")]
[Authorize]
public class SdrLeadsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly TenantUnitGuard _tenantGuard;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<SdrLeadsController> _logger;

    public SdrLeadsController(
        AppDbContext db,
        TenantUnitGuard tenantGuard,
        ICurrentUser currentUser,
        ILogger<SdrLeadsController> logger)
    {
        _db = db;
        _tenantGuard = tenantGuard;
        _currentUser = currentUser;
        _logger = logger;
    }

    /// <summary>
    /// POST /api/sdr/leads/sync-from-cloudia
    /// Lê todos os leads do tenant atual (que entraram via webhook Cloudia)
    /// e devolve no formato SdrLeadResponseDto para o front.
    /// </summary>
    [HttpPost("sync-from-cloudia")]
    public async Task<IActionResult> SyncFromCloudia(CancellationToken ct)
    {
        if (_tenantGuard.RequireTenant(out var tenantId) is { } denied) return denied;

        var query = _db.Leads
            .AsNoTracking()
            .Include(l => l.Unit)
            .Include(l => l.Attendant)
            .AsQueryable();

        if (tenantId.HasValue)
            query = query.Where(l => l.TenantId == tenantId.Value);

        // Limita pra não estourar memória/payload — recentes primeiro
        var leads = await query
            .OrderByDescending(l => l.CreatedAt)
            .Take(500)
            .ToListAsync(ct);

        var items = leads.Select(MapToSdrDto).ToList();

        _logger.LogInformation(
            "🔄 SDR sync: tenant={Tenant}, count={Count}",
            tenantId, items.Count);

        return Ok(new SdrSyncSummaryDto
        {
            Created = items.Count,
            Skipped = 0,
            Updated = 0,
            Failed = 0,
            Items = items
        });
    }

    /// <summary>
    /// Mapeia Lead (gravado pelo webhook Cloudia) para o DTO esperado pela
    /// tela de revisão SDR. Os campos que vêm da Cloudia ficam listados em
    /// <c>CloudiaFields</c> — o front usa isso para colorir as células com
    /// o indicador "vindo da Cloudia".
    /// </summary>
    private static SdrLeadResponseDto MapToSdrDto(Models.Lead l)
    {
        // Quais campos vêm efetivamente do webhook Cloudia.
        // Não-nulos/não-default → marcados.
        var cf = new List<string>();
        if (!string.IsNullOrWhiteSpace(l.Name))         cf.Add("nome");
        if (!string.IsNullOrWhiteSpace(l.Phone))        cf.Add("telefone");
        if (!string.IsNullOrWhiteSpace(l.Source) && l.Source != "DESCONHECIDO") cf.Add("origem");
        if (!string.IsNullOrWhiteSpace(l.Observations)) cf.Add("observacao");
        if (l.HasAppointment)                            cf.Add("agendouConsulta");
        if (!string.IsNullOrWhiteSpace(l.CurrentStage)) cf.Add("situacao");
        cf.Add("dataOrigem");
        if (l.UpdatedAt > l.CreatedAt) cf.Add("dataModificacao");

        return new SdrLeadResponseDto
        {
            Id = l.Id,
            TenantId = l.TenantId,
            ExternalId = l.ExternalId,
            Nome = l.Name ?? string.Empty,
            Telefone = l.Phone ?? string.Empty,
            Tipo = "Cadastro",
            Origem = l.Source ?? "Cloudia",
            Interacao = false,
            AgendouConsulta = l.HasAppointment,
            DataAgendamento = null,
            NomeResponsavel = l.Attendant?.Name ?? string.Empty,
            Observacao = l.Observations,
            Situacao = l.CurrentStage,
            Clinica = l.Unit?.Name,
            DataOrigem = l.CreatedAt.ToString("o"),
            DataModificacao = l.UpdatedAt.ToString("o"),
            Source = "cloudia",
            Status = "pendente_revisao",
            CloudiaFields = cf,
            CloudiaReceivedAt = l.CreatedAt.ToString("o"),
            CloudiaWebhookEvent = "CUSTOMER_CREATED",
            UnitId = l.UnitId,
            AttendantId = l.AttendantId,
            CreatedAt = l.CreatedAt.ToString("o"),
            UpdatedAt = l.UpdatedAt.ToString("o"),
        };
    }
}
