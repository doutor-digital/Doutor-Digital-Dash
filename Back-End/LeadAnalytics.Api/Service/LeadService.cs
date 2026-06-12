using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.DTOs.Response;
using LeadAnalytics.Api.Models;
using Microsoft.EntityFrameworkCore;
using System.Security.AccessControl;
using System.Text.Json;

namespace LeadAnalytics.Api.Service;

public class LeadService(
    AppDbContext db,
    ILogger<LeadService> logger,
    UnitService unitService,
    AttendantService attendantService,
    LeadAttributionService attributionService)
{
    private readonly AppDbContext _db = db;
    private readonly ILogger<LeadService> _logger = logger;
    private readonly UnitService _unitService = unitService;
    private readonly AttendantService _attendantService = attendantService;
    private readonly LeadAttributionService _attributionService = attributionService;

    public async Task<List<Lead>> GetAllLeadsAsync(int? tenantId, int? unitId = null)
    {
        var query = _db.Leads.AsNoTracking().AsQueryable();

        if (tenantId.HasValue)
            query = query.Where(l => l.TenantId == tenantId.Value);

        if (unitId.HasValue)
            query = query.Where(l => l.UnitId == unitId.Value);

        return await query.ToListAsync();
    }

    public async Task<LeadDetailDto?> GetLeadByIdAsync(int id, int? tenantId, int? unitId = null)
    {
        var query = _db.Leads
            .AsNoTracking()
            .Include(l => l.Unit)
            .Include(l => l.Attendant)
            .Include(l => l.StageHistory)
            .Include(l => l.Conversations)
                .ThenInclude(c => c.Interactions)
            .Include(l => l.Conversations)
                .ThenInclude(c => c.Attendant)
            .Include(l => l.Assignments)
                .ThenInclude(a => a.Attendant)
            .Include(l => l.Payments)
            .Include(l => l.PaymentReceipts)
            .Where(l => l.Id == id);

        if (unitId.HasValue)
            query = query.Where(l => l.UnitId == unitId.Value);

        var lead = await query.FirstOrDefaultAsync();

        if (lead is null) return null;

        var tags = new List<string>();
        if (!string.IsNullOrWhiteSpace(lead.Tags))
        {
            try
            {
                tags = JsonSerializer.Deserialize<List<string>>(lead.Tags) ?? new();
            }
            catch
            {
                tags = new List<string> { lead.Tags };
            }
        }

        return new LeadDetailDto
        {
            Id = lead.Id,
            ExternalId = lead.ExternalId,
            TenantId = lead.TenantId,

            Name = lead.Name,
            Phone = lead.Phone == "AGUARDANDO_COLETA" ? null : lead.Phone,
            Email = lead.Email == "AGUARDANDO_COLETA" ? null : lead.Email,
            Cpf = lead.Cpf,
            Gender = lead.Gender,

            Source = lead.Source,
            Channel = lead.Channel,
            Campaign = lead.Campaign,
            Ad = lead.Ad,
            TrackingConfidence = lead.TrackingConfidence,

            CurrentStage = lead.CurrentStage,
            CurrentStageId = lead.CurrentStageId,
            Status = lead.Status,
            ConversationState = lead.ConversationState,

            HasAppointment = lead.HasAppointment,
            HasPayment = lead.HasPayment,
            HasHealthInsurancePlan = lead.HasHealthInsurancePlan,
            Observations = lead.Observations,
            Tags = tags,

            AttendanceStatus = lead.AttendanceStatus,
            AttendanceStatusAt = lead.AttendanceStatusAt,

            UnitId = lead.UnitId,
            UnitName = lead.Unit?.Name,

            AttendantId = lead.AttendantId,
            AttendantName = lead.Attendant?.Name,
            AttendantEmail = lead.Attendant?.Email,

            CreatedAt = lead.CreatedAt,
            UpdatedAt = lead.UpdatedAt,
            ConvertedAt = lead.ConvertedAt,

            StageHistory = lead.StageHistory
                .OrderBy(h => h.ChangedAt)
                .Select(h => new LeadStageHistoryDto
                {
                    Id = h.Id,
                    StageId = h.StageId,
                    StageLabel = h.StageLabel,
                    ChangedAt = h.ChangedAt
                })
                .ToList(),

            Conversations = lead.Conversations
                .OrderBy(c => c.StartedAt)
                .Select(c => new LeadConversationDto
                {
                    Id = c.Id,
                    Channel = c.Channel,
                    Source = c.Source,
                    ConversationState = c.ConversationState,
                    StartedAt = c.StartedAt,
                    EndedAt = c.EndedAt,
                    AttendantId = c.AttendantId,
                    AttendantName = c.Attendant?.Name,
                    Interactions = c.Interactions
                        .OrderBy(i => i.CreatedAt)
                        .Select(i => new LeadInteractionDto
                        {
                            Id = i.Id,
                            Type = i.Type,
                            Content = i.Content,
                            CreatedAt = i.CreatedAt
                        })
                        .ToList()
                })
                .ToList(),

            Assignments = lead.Assignments
                .OrderByDescending(a => a.AssignedAt)
                .Select(a => new LeadAssignmentDto
                {
                    Id = a.Id,
                    AttendantId = a.AttendantId,
                    AttendantName = a.Attendant?.Name,
                    Stage = a.Stage,
                    AssignedAt = a.AssignedAt
                })
                .ToList(),

            Payments = lead.Payments
                .OrderByDescending(p => p.PaidAt)
                .Select(p => new LeadPaymentDto
                {
                    Id = p.Id,
                    Amount = p.Amount,
                    PaidAt = p.PaidAt
                })
                .ToList(),

            // ─── Revisão comercial ──────────────────────
            LeadType = lead.LeadType,
            RescueType = lead.RescueType,
            HadInteraction = lead.HadInteraction,
            ScheduledConsultation = lead.ScheduledConsultation,
            AppointmentScheduledAt = lead.AppointmentScheduledAt,
            NoAppointmentReason = lead.NoAppointmentReason,
            NoAppointmentCity = lead.NoAppointmentCity,
            NoCloseReason = lead.NoCloseReason,
            ConsultationValue = lead.ConsultationValue,
            ClosedTreatment = lead.ClosedTreatment,
            IndicatedTreatment = lead.IndicatedTreatment,
            TreatmentBudget = lead.TreatmentBudget,
            TreatmentPlanCategory = lead.TreatmentPlanCategory,
            TreatmentPlanValue = lead.TreatmentPlanValue,

            PaymentReceipts = lead.PaymentReceipts
                .OrderBy(r => r.Kind).ThenBy(r => r.Slot)
                .Select(r => new LeadPaymentReceiptDto
                {
                    Id = r.Id,
                    Kind = r.Kind,
                    Slot = r.Slot,
                    Amount = r.Amount,
                    Method = r.Method,
                    ReceivedAt = r.ReceivedAt,
                    IsAdvance = r.IsAdvance,
                })
                .ToList(),
        };
    }

    // ════════════════════════════════════════════════════════════════
    //  PATCH /webhooks/{id}: atualização parcial do lead + receipts
    // ════════════════════════════════════════════════════════════════

    private static readonly string[] _allowedRescueTypes = { "mensagem", "ligacao", "disparo_massa" };
    private static readonly string[] _allowedLeadTypes = { "cadastro", "resgate" };
    private static readonly string[] _allowedReceiptKinds = { "consulta", "tratamento" };

    public async Task<LeadDetailDto> PatchLeadAsync(
        int id, int tenantId, DTOs.Request.UpdateLeadDto dto, CancellationToken ct = default)
    {
        var lead = await _db.Leads
            .Include(l => l.PaymentReceipts)
            .FirstOrDefaultAsync(l => l.Id == id && l.TenantId == tenantId, ct)
            ?? throw new ArgumentException("Lead não encontrado para este tenant");

        // ── Identificação
        if (dto.Name is not null) lead.Name = dto.Name;
        if (dto.Phone is not null) lead.Phone = dto.Phone;
        if (dto.Email is not null) lead.Email = string.IsNullOrWhiteSpace(dto.Email) ? null : dto.Email;
        if (dto.Cpf is not null) lead.Cpf = string.IsNullOrWhiteSpace(dto.Cpf) ? null : dto.Cpf;
        if (dto.Gender is not null) lead.Gender = string.IsNullOrWhiteSpace(dto.Gender) ? null : dto.Gender;

        // ── Atribuição
        if (dto.Source is not null) lead.Source = dto.Source;
        if (dto.Channel is not null) lead.Channel = dto.Channel;
        if (dto.Campaign is not null) lead.Campaign = dto.Campaign;
        if (dto.Ad is not null) lead.Ad = string.IsNullOrWhiteSpace(dto.Ad) ? null : dto.Ad;

        // ── Estado
        if (dto.CurrentStage is not null) lead.CurrentStage = dto.CurrentStage;
        if (dto.HasAppointment.HasValue) lead.HasAppointment = dto.HasAppointment.Value;
        if (dto.HasPayment.HasValue) lead.HasPayment = dto.HasPayment.Value;
        if (dto.HasHealthInsurancePlan.HasValue) lead.HasHealthInsurancePlan = dto.HasHealthInsurancePlan.Value;
        if (dto.Observations is not null) lead.Observations = string.IsNullOrWhiteSpace(dto.Observations) ? null : dto.Observations;
        if (dto.Tags is not null) lead.Tags = JsonSerializer.Serialize(dto.Tags);

        if (dto.UnitId.HasValue) lead.UnitId = dto.UnitId.Value == 0 ? null : dto.UnitId.Value;
        if (dto.AttendantId.HasValue) lead.AttendantId = dto.AttendantId.Value == 0 ? null : dto.AttendantId.Value;

        // ── Revisão comercial
        if (dto.LeadType is not null)
        {
            var lt = dto.LeadType.Trim().ToLowerInvariant();
            if (lt.Length == 0) lead.LeadType = null;
            else if (!_allowedLeadTypes.Contains(lt))
                throw new ArgumentException($"leadType inválido (use {string.Join("|", _allowedLeadTypes)})");
            else lead.LeadType = lt;
        }
        if (dto.RescueType is not null)
        {
            var rt = dto.RescueType.Trim().ToLowerInvariant();
            if (rt.Length == 0) lead.RescueType = null;
            else if (!_allowedRescueTypes.Contains(rt))
                throw new ArgumentException($"rescueType inválido (use {string.Join("|", _allowedRescueTypes)})");
            else lead.RescueType = rt;
        }
        if (dto.HadInteraction.HasValue) lead.HadInteraction = dto.HadInteraction.Value;
        if (dto.ScheduledConsultation.HasValue) lead.ScheduledConsultation = dto.ScheduledConsultation.Value;
        if (dto.AppointmentScheduledAt.HasValue) lead.AppointmentScheduledAt = dto.AppointmentScheduledAt.Value;
        if (dto.NoAppointmentReason is not null) lead.NoAppointmentReason = string.IsNullOrWhiteSpace(dto.NoAppointmentReason) ? null : dto.NoAppointmentReason;
        if (dto.NoAppointmentCity is not null) lead.NoAppointmentCity = string.IsNullOrWhiteSpace(dto.NoAppointmentCity) ? null : dto.NoAppointmentCity;
        if (dto.NoCloseReason is not null) lead.NoCloseReason = string.IsNullOrWhiteSpace(dto.NoCloseReason) ? null : dto.NoCloseReason;
        if (dto.ConsultationValue.HasValue) lead.ConsultationValue = dto.ConsultationValue.Value;
        if (dto.ClosedTreatment.HasValue) lead.ClosedTreatment = dto.ClosedTreatment.Value;
        if (dto.IndicatedTreatment is not null) lead.IndicatedTreatment = string.IsNullOrWhiteSpace(dto.IndicatedTreatment) ? null : dto.IndicatedTreatment;
        if (dto.TreatmentBudget.HasValue) lead.TreatmentBudget = dto.TreatmentBudget.Value;
        if (dto.TreatmentPlanCategory is not null) lead.TreatmentPlanCategory = string.IsNullOrWhiteSpace(dto.TreatmentPlanCategory) ? null : dto.TreatmentPlanCategory;
        if (dto.TreatmentPlanValue.HasValue) lead.TreatmentPlanValue = dto.TreatmentPlanValue.Value;

        // ── Receipts (replace strategy)
        if (dto.PaymentReceipts is not null)
        {
            // valida
            foreach (var r in dto.PaymentReceipts)
            {
                var k = (r.Kind ?? "").Trim().ToLowerInvariant();
                if (!_allowedReceiptKinds.Contains(k))
                    throw new ArgumentException($"receipt.kind inválido (use {string.Join("|", _allowedReceiptKinds)})");
                if (k == "consulta" && (r.Slot < 1 || r.Slot > 2))
                    throw new ArgumentException("receipt.slot de consulta deve ser 1..2");
                if (k == "tratamento" && (r.Slot < 1 || r.Slot > 6))
                    throw new ArgumentException("receipt.slot de tratamento deve ser 1..6");
            }

            // remove os antigos
            _db.LeadPaymentReceipts.RemoveRange(lead.PaymentReceipts);

            // recria
            foreach (var r in dto.PaymentReceipts)
            {
                // se a linha está totalmente vazia (sem amount nem data nem method), pula
                if (r.Amount is null && r.ReceivedAt is null && string.IsNullOrWhiteSpace(r.Method)) continue;

                _db.LeadPaymentReceipts.Add(new LeadPaymentReceipt
                {
                    LeadId = lead.Id,
                    TenantId = tenantId,
                    Kind = r.Kind.Trim().ToLowerInvariant(),
                    Slot = r.Slot,
                    Amount = r.Amount,
                    Method = string.IsNullOrWhiteSpace(r.Method) ? null : r.Method.Trim().ToLowerInvariant(),
                    ReceivedAt = r.ReceivedAt,
                    IsAdvance = r.IsAdvance,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                });
            }
        }

        lead.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        return (await GetLeadByIdAsync(id, tenantId))!;
    }


    public async Task<LeadProcessResponseDto> MarkAttendanceAsync(
        int leadId, int tenantId, MarkAttendanceDto dto, CancellationToken ct = default)
    {
        var lead = await _db.Leads
            .Include(l => l.StageHistory)
            .Include(l => l.Conversations).ThenInclude(c => c.Interactions)
            .FirstOrDefaultAsync(l => l.Id == leadId && l.TenantId == tenantId, ct)
            ?? throw new InvalidOperationException($"Lead {leadId} não encontrado para clínica {tenantId}");

        if (!LeadStages.IsScheduled(lead.CurrentStage))
            throw new InvalidOperationException(
                $"Comparecimento só pode ser marcado em leads com consulta agendada. Etapa atual: {lead.CurrentStage}");

        string newStage;
        string newAttendance;
        string interactionType;
        string interactionContent;
        var now = DateTime.UtcNow;

        if (!dto.Attended)
        {
            newStage = LeadStages.Faltou;
            newAttendance = LeadStages.AttendedFaltou;
            interactionType = "ATTENDANCE_NO_SHOW";
            interactionContent = "Paciente não compareceu";
        }
        else
        {
            var outcome = (dto.Outcome ?? "").Trim().ToLowerInvariant();
            if (outcome != "fechou" && outcome != "nao_fechou")
                throw new ArgumentException("outcome deve ser 'fechou' ou 'nao_fechou' quando attended=true");

            if (outcome == "fechou")
            {
                newStage = LeadStages.FechouTratamento;
                interactionType = "ATTENDANCE_CLOSED";
                interactionContent = "Compareceu e fechou tratamento";
            }
            else
            {
                newStage = LeadStages.NaoFechouTratamento;
                interactionType = "ATTENDANCE_NOT_CLOSED";
                interactionContent = "Compareceu e não fechou tratamento";
            }
            newAttendance = LeadStages.AttendedCompareceu;
        }

        if (!string.IsNullOrWhiteSpace(dto.Notes))
            interactionContent += $" — {dto.Notes.Trim()}";

        var prevStage = lead.CurrentStage;

        lead.StageHistory.Add(new LeadStageHistory
        {
            LeadId = lead.Id,
            StageId = lead.CurrentStageId ?? 0,
            StageLabel = newStage,
            ChangedAt = now,
        });

        var conversaAtiva = lead.Conversations.FirstOrDefault(c => c.EndedAt is null);
        conversaAtiva?.Interactions.Add(new LeadInteraction
        {
            Type = interactionType,
            Content = interactionContent,
            CreatedAt = now,
        });

        lead.CurrentStage = newStage;
        lead.AttendanceStatus = newAttendance;
        lead.AttendanceStatusAt = now;
        lead.HasAppointment = true;
        lead.UpdatedAt = now;

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Comparecimento registrado: lead={LeadId} {Prev}→{Next} attendance={Attendance}",
            lead.Id, prevStage, newStage, newAttendance);

        return new LeadProcessResponseDto
        {
            LeadId = lead.Id,
            Message = $"Comparecimento registrado: {newAttendance} → {newStage}",
            Result = ProcessResult.Updated,
            Source = lead.Source,
            TrackingConfidence = lead.TrackingConfidence,
        };
    }

    public async Task<StageChangesSummaryDto> GetStageChangesAsync(
        int clinicId,
        DateTime? dateFrom,
        DateTime? dateTo,
        int? unitId,
        int limit = 100,
        CancellationToken ct = default)
    {
        if (limit <= 0) limit = 100;
        if (limit > 500) limit = 500;

        var fromUtc = ToUtcStart(dateFrom);
        var toUtc = ToUtcEndExclusive(dateTo);

        var q = _db.LeadStageHistories.AsNoTracking()
            .Include(h => h.Lead)
                .ThenInclude(l => l.Unit)
            .Where(h => h.Lead.TenantId == clinicId);

        if (unitId.HasValue) q = q.Where(h => h.Lead.UnitId == unitId.Value);
        if (fromUtc.HasValue) q = q.Where(h => h.ChangedAt >= fromUtc.Value);
        if (toUtc.HasValue) q = q.Where(h => h.ChangedAt < toUtc.Value);

        var total = await q.CountAsync(ct);

        var daily = await q
            .GroupBy(h => h.ChangedAt.Date)
            .Select(g => new StageChangeDailyPointDto { Date = g.Key, Count = g.Count() })
            .OrderBy(x => x.Date)
            .ToListAsync(ct);

        var byDestination = await q
            .GroupBy(h => h.StageLabel)
            .Select(g => new StageChangeDestinationDto { Stage = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .ToListAsync(ct);

        // Para "FromStage" precisamos olhar a entrada anterior do mesmo lead.
        // Fazemos isso em memória com window function via ordenação por LeadId/ChangedAt.
        var raw = await q
            .OrderByDescending(h => h.ChangedAt)
            .Take(limit)
            .Select(h => new
            {
                h.Id,
                h.LeadId,
                LeadName = h.Lead.Name,
                LeadPhone = h.Lead.Phone == "AGUARDANDO_COLETA" ? null : h.Lead.Phone,
                UnitId = h.Lead.UnitId,
                UnitName = h.Lead.Unit != null ? h.Lead.Unit.Name : null,
                Source = h.Lead.Source,
                ToStage = h.StageLabel,
                h.ChangedAt,
            })
            .ToListAsync(ct);

        var leadIds = raw.Select(x => x.LeadId).Distinct().ToList();
        var historiesByLead = await _db.LeadStageHistories.AsNoTracking()
            .Where(h => leadIds.Contains(h.LeadId))
            .OrderBy(h => h.LeadId).ThenBy(h => h.ChangedAt)
            .Select(h => new { h.Id, h.LeadId, h.StageLabel, h.ChangedAt })
            .ToListAsync(ct);

        var prevByEntry = historiesByLead
            .GroupBy(h => h.LeadId)
            .ToDictionary(g => g.Key, g => g.OrderBy(x => x.ChangedAt).ToList());

        var items = raw.Select(r =>
        {
            string? from = null;
            if (prevByEntry.TryGetValue(r.LeadId, out var list))
            {
                var idx = list.FindIndex(x => x.Id == r.Id);
                if (idx > 0) from = list[idx - 1].StageLabel;
            }
            return new StageChangeDto
            {
                Id = r.Id,
                LeadId = r.LeadId,
                LeadName = r.LeadName,
                LeadPhone = r.LeadPhone,
                UnitId = r.UnitId,
                UnitName = r.UnitName,
                Source = r.Source,
                FromStage = from,
                ToStage = r.ToStage,
                ChangedAt = r.ChangedAt,
            };
        }).ToList();

        return new StageChangesSummaryDto
        {
            Total = total,
            Daily = daily,
            ByDestination = byDestination,
            Items = items,
        };
    }

    private static DateTime? ToUtcStart(DateTime? value)
    {
        if (!value.HasValue) return null;
        var d = value.Value.Date;
        return DateTime.SpecifyKind(d, DateTimeKind.Utc);
    }

    private static DateTime? ToUtcEndExclusive(DateTime? value)
    {
        if (!value.HasValue) return null;
        var d = value.Value.Date.AddDays(1);
        return DateTime.SpecifyKind(d, DateTimeKind.Utc);
    }

    public async Task<ConversionAnalyticsDto> GetConversionAnalyticsAsync(
        int clinicId,
        DateTime? dateFrom,
        DateTime? dateTo,
        int? unitId,
        CancellationToken ct = default)
    {
        var fromUtc = ToUtcStart(dateFrom) ?? DateTime.UtcNow.Date.AddDays(-30);
        var toUtcExcl = ToUtcEndExclusive(dateTo) ?? DateTime.UtcNow.Date.AddDays(1);

        var baseQ = _db.Leads.AsNoTracking()
            .Where(l => l.TenantId == clinicId
                     && l.CreatedAt >= fromUtc
                     && l.CreatedAt < toUtcExcl);

        if (unitId.HasValue) baseQ = baseQ.Where(l => l.UnitId == unitId.Value);

        var totalEntradas = await baseQ.CountAsync(ct);

        // Converte = chegou em estágio de fechamento OU tem ConvertedAt
        var convertidosQ = baseQ.Where(l =>
            l.ConvertedAt != null
            || l.CurrentStage == LeadStages.FechouTratamento
            || l.CurrentStage == LeadStages.EmTratamento);

        var totalConvertidos = await convertidosQ.CountAsync(ct);

        // Não converte = chegou em estágio terminal sem fechar
        var naoConvertidosQ = baseQ.Where(l =>
            l.CurrentStage == LeadStages.NaoFechouTratamento
            || l.CurrentStage == LeadStages.Faltou);

        var totalNaoConvertidos = await naoConvertidosQ.CountAsync(ct);

        var totalEmAndamento = totalEntradas - totalConvertidos - totalNaoConvertidos;
        if (totalEmAndamento < 0) totalEmAndamento = 0;

        var taxaConversao = totalEntradas > 0
            ? Math.Round(totalConvertidos * 100.0 / totalEntradas, 2)
            : 0;
        var taxaNaoConversao = totalEntradas > 0
            ? Math.Round(totalNaoConvertidos * 100.0 / totalEntradas, 2)
            : 0;

        // Tempo até conversão (em dias) — média e mediana. Calculamos em memória
        // porque o Postgres não tem DateDiffDay; o conjunto é pequeno (só convertidos).
        var convertidosDatas = await convertidosQ
            .Where(l => l.ConvertedAt != null)
            .Select(l => new { l.CreatedAt, ConvertedAt = l.ConvertedAt!.Value })
            .ToListAsync(ct);

        var convertidosTempos = convertidosDatas
            .Select(x => (x.ConvertedAt - x.CreatedAt).TotalDays)
            .ToList();

        double? mediaDias = convertidosTempos.Count > 0
            ? Math.Round(convertidosTempos.Average(), 1)
            : null;
        double? medianaDias = null;
        if (convertidosTempos.Count > 0)
        {
            var ord = convertidosTempos.OrderBy(x => x).ToList();
            medianaDias = ord.Count % 2 == 1
                ? Math.Round(ord[ord.Count / 2], 1)
                : Math.Round((ord[ord.Count / 2 - 1] + ord[ord.Count / 2]) / 2.0, 1);
        }

        // Pega não convertidos com observações pra classificar motivos
        var naoConvertidos = await naoConvertidosQ
            .OrderByDescending(l => l.UpdatedAt)
            .Select(l => new
            {
                l.Id,
                l.Name,
                Phone = l.Phone == "AGUARDANDO_COLETA" ? null : l.Phone,
                l.CurrentStage,
                l.Observations,
                l.Source,
                l.CreatedAt,
                l.UpdatedAt,
            })
            .ToListAsync(ct);

        // Classificação heurística em memória
        var classificados = naoConvertidos.Select(l => new
        {
            l.Id,
            l.Name,
            l.Phone,
            l.CurrentStage,
            l.Observations,
            l.Source,
            l.CreatedAt,
            l.UpdatedAt,
            MotivoKey = RejectionReasons.Classify(l.Observations),
        }).ToList();

        var motivoGroups = classificados
            .GroupBy(c => c.MotivoKey ?? "sem_motivo")
            .Select(g =>
            {
                var cat = g.Key == "sem_motivo" ? null : RejectionReasons.Get(g.Key);
                return new NaoConversaoMotivoDto
                {
                    Motivo = cat?.Label ?? "Sem motivo registrado",
                    Categoria = g.Key,
                    Quantidade = g.Count(),
                    Percentual = totalNaoConvertidos > 0
                        ? Math.Round(g.Count() * 100.0 / totalNaoConvertidos, 2)
                        : 0,
                    PalavrasChave = cat?.Keywords.Take(4).ToList() ?? new List<string>(),
                };
            })
            .OrderByDescending(m => m.Quantidade)
            .ToList();

        // Lista de exemplos (top 30 não convertidos com observação preenchida)
        var exemplos = classificados
            .Where(l => !string.IsNullOrWhiteSpace(l.Observations))
            .Take(30)
            .Select(l =>
            {
                var cat = l.MotivoKey is null ? null : RejectionReasons.Get(l.MotivoKey);
                return new NaoConvertidoItemDto
                {
                    LeadId = l.Id,
                    Name = l.Name,
                    Phone = l.Phone,
                    CurrentStage = l.CurrentStage,
                    Observations = l.Observations,
                    MotivoCategoria = cat?.Label,
                    Source = l.Source,
                    CreatedAt = l.CreatedAt,
                    UpdatedAt = l.UpdatedAt,
                };
            })
            .ToList();

        // Funil bruto: contagem por etapa atual no período
        var funil = await baseQ
            .GroupBy(l => string.IsNullOrWhiteSpace(l.CurrentStage) ? "SEM_ETAPA" : l.CurrentStage)
            .Select(g => new ConversaoFunilEtapaDto { Stage = g.Key, Quantidade = g.Count() })
            .OrderBy(g => g.Stage)
            .ToListAsync(ct);

        return new ConversionAnalyticsDto
        {
            DateFrom = fromUtc,
            DateTo = toUtcExcl.AddDays(-1),
            TotalEntradas = totalEntradas,
            TotalConvertidos = totalConvertidos,
            TotalNaoConvertidos = totalNaoConvertidos,
            TotalEmAndamento = totalEmAndamento,
            TaxaConversao = taxaConversao,
            TaxaNaoConversao = taxaNaoConversao,
            MediaDiasAteConversao = mediaDias,
            MedianaDiasAteConversao = medianaDias,
            Motivos = motivoGroups,
            Exemplos = exemplos,
            Funil = funil,
        };
    }

    public async Task<List<RecoveryLeadDto>> GetRecoveryQueueAsync(
        int clinicId,
        int? unitId = null,
        DateTime? dateFrom = null,
        DateTime? dateTo = null,
        int? attendantId = null,
        string? attemptsFilter = null, // "with" | "without" | null
        CancellationToken ct = default)
    {
        var q = _db.Leads.AsNoTracking()
            .Include(l => l.Unit)
            .Include(l => l.Attendant)
            .Where(l => l.TenantId == clinicId
                     && l.CurrentStage == LeadStages.NaoFechouTratamento);

        if (unitId.HasValue) q = q.Where(l => l.UnitId == unitId.Value);
        if (attendantId.HasValue) q = q.Where(l => l.AttendantId == attendantId.Value);

        if (dateFrom.HasValue)
        {
            var fromUtc = DateTime.SpecifyKind(dateFrom.Value.Date, DateTimeKind.Utc);
            q = q.Where(l => (l.AttendanceStatusAt ?? l.UpdatedAt) >= fromUtc);
        }
        if (dateTo.HasValue)
        {
            var toExclUtc = DateTime.SpecifyKind(dateTo.Value.Date.AddDays(1), DateTimeKind.Utc);
            q = q.Where(l => (l.AttendanceStatusAt ?? l.UpdatedAt) < toExclUtc);
        }

        // Sub-query de attempts por lead
        var attempts = _db.RecoveryAttempts.AsNoTracking()
            .Where(a => a.TenantId == clinicId);

        var projected = q.Select(l => new
        {
            Lead = l,
            AttemptsCount = attempts.Count(a => a.LeadId == l.Id),
            LastAttempt = attempts
                .Where(a => a.LeadId == l.Id)
                .OrderByDescending(a => a.CreatedAt)
                .FirstOrDefault(),
        });

        if (attemptsFilter == "with") projected = projected.Where(x => x.AttemptsCount > 0);
        else if (attemptsFilter == "without") projected = projected.Where(x => x.AttemptsCount == 0);

        var rows = await projected
            .OrderByDescending(x => x.Lead.AttendanceStatusAt ?? x.Lead.UpdatedAt)
            .ToListAsync(ct);

        return rows.Select(x => new RecoveryLeadDto
        {
            Id = x.Lead.Id,
            Name = x.Lead.Name,
            Phone = x.Lead.Phone == "AGUARDANDO_COLETA" ? null : x.Lead.Phone,
            UnitId = x.Lead.UnitId,
            UnitName = x.Lead.Unit?.Name,
            Source = x.Lead.Source,
            Campaign = x.Lead.Campaign,
            AttendantId = x.Lead.AttendantId,
            AttendantName = x.Lead.Attendant?.Name,
            AttendanceStatusAt = x.Lead.AttendanceStatusAt,
            UpdatedAt = x.Lead.UpdatedAt,
            AttemptsCount = x.AttemptsCount,
            LastAttemptAt = x.LastAttempt?.CreatedAt,
            LastAttemptOutcome = x.LastAttempt?.Outcome,
        }).ToList();
    }

    // ════════════════════════════════════════════════════════════════
    //  Tentativas de recuperação (histórico estruturado)
    // ════════════════════════════════════════════════════════════════

    private static readonly string[] _allowedMethods = { "whatsapp", "call", "email", "visit", "other" };
    private static readonly string[] _allowedOutcomes = { "no_answer", "scheduled", "recovered", "lost", "follow_up" };

    public async Task<List<RecoveryAttemptDto>> ListRecoveryAttemptsAsync(
        int leadId, int clinicId, CancellationToken ct = default)
    {
        return await _db.RecoveryAttempts.AsNoTracking()
            .Where(a => a.LeadId == leadId && a.TenantId == clinicId)
            .OrderByDescending(a => a.CreatedAt)
            .Select(a => new RecoveryAttemptDto
            {
                Id = a.Id,
                LeadId = a.LeadId,
                Method = a.Method,
                Outcome = a.Outcome,
                Notes = a.Notes,
                AttendantId = a.AttendantId,
                AttendantName = a.AttendantId != null
                    ? _db.Attendants.Where(at => at.Id == a.AttendantId).Select(at => at.Name).FirstOrDefault()
                    : null,
                CreatedByUserId = a.CreatedByUserId,
                CreatedAt = a.CreatedAt,
            })
            .ToListAsync(ct);
    }

    public async Task<RecoveryAttemptDto> CreateRecoveryAttemptAsync(
        int leadId, int clinicId, CreateRecoveryAttemptDto dto, int? createdByUserId, CancellationToken ct = default)
    {
        var lead = await _db.Leads.FirstOrDefaultAsync(l => l.Id == leadId && l.TenantId == clinicId, ct)
            ?? throw new ArgumentException("Lead não encontrado para este tenant");

        var method = (dto.Method ?? "").Trim().ToLowerInvariant();
        var outcome = (dto.Outcome ?? "").Trim().ToLowerInvariant();
        if (!_allowedMethods.Contains(method))
            throw new ArgumentException($"method inválido (use {string.Join("|", _allowedMethods)})");
        if (!_allowedOutcomes.Contains(outcome))
            throw new ArgumentException($"outcome inválido (use {string.Join("|", _allowedOutcomes)})");

        var attempt = new RecoveryAttempt
        {
            LeadId = leadId,
            TenantId = clinicId,
            Method = method,
            Outcome = outcome,
            Notes = string.IsNullOrWhiteSpace(dto.Notes) ? null : dto.Notes.Trim(),
            AttendantId = lead.AttendantId,
            CreatedByUserId = createdByUserId,
            CreatedAt = DateTime.UtcNow,
        };

        _db.RecoveryAttempts.Add(attempt);
        await _db.SaveChangesAsync(ct);

        return new RecoveryAttemptDto
        {
            Id = attempt.Id,
            LeadId = attempt.LeadId,
            Method = attempt.Method,
            Outcome = attempt.Outcome,
            Notes = attempt.Notes,
            AttendantId = attempt.AttendantId,
            CreatedByUserId = attempt.CreatedByUserId,
            CreatedAt = attempt.CreatedAt,
        };
    }

    public async Task<RecoveryAttemptDto> MarkRecoveredAsync(
        int leadId, int clinicId, string? notes, int? createdByUserId, CancellationToken ct = default)
    {
        var lead = await _db.Leads.FirstOrDefaultAsync(l => l.Id == leadId && l.TenantId == clinicId, ct)
            ?? throw new ArgumentException("Lead não encontrado para este tenant");

        // Move o lead pra FechouTratamento (recuperação concretizada)
        lead.CurrentStage = LeadStages.FechouTratamento;
        lead.UpdatedAt = DateTime.UtcNow;

        var attempt = new RecoveryAttempt
        {
            LeadId = leadId,
            TenantId = clinicId,
            Method = "other",
            Outcome = "recovered",
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
            AttendantId = lead.AttendantId,
            CreatedByUserId = createdByUserId,
            CreatedAt = DateTime.UtcNow,
        };
        _db.RecoveryAttempts.Add(attempt);

        await _db.SaveChangesAsync(ct);

        return new RecoveryAttemptDto
        {
            Id = attempt.Id,
            LeadId = attempt.LeadId,
            Method = attempt.Method,
            Outcome = attempt.Outcome,
            Notes = attempt.Notes,
            AttendantId = attempt.AttendantId,
            CreatedByUserId = attempt.CreatedByUserId,
            CreatedAt = attempt.CreatedAt,
        };
    }

   public async Task<int> GetLeadsTotal(int? clinicId)
    {
        return await _db.Leads.AsNoTracking()
        .Where(l => !clinicId.HasValue || l.TenantId == clinicId.Value).CountAsync();
    }
    
    /// <summary>
    /// Contar leads em atendimento (estado "service")
    /// </summary>
    public async Task<int> GetLeadsInServiceCountAsync(int? tenantId, int? unitId = null)
    {
        var query = _db.Leads
            .AsNoTracking()
            .Where(l => l.ConversationState == "service");

        if (tenantId.HasValue)
            query = query.Where(l => l.TenantId == tenantId.Value);

        if (unitId.HasValue)
            query = query.Where(l => l.UnitId == unitId.Value);

        var count = await query.CountAsync();

        _logger.LogInformation(
            "📊 Leads em atendimento: {Count} (tenantId={TenantId}, unitId={UnitId})",
            count, tenantId, unitId);

        return count;
    }

    /// <summary>
    /// Contar leads em cada estado
    /// </summary>
    public async Task<LeadsInServiceDto> GetLeadsInServiceDetailsAsync(int? tenantId, int? unitId = null)
    {
        var query = _db.Leads.AsNoTracking().AsQueryable();

        if (tenantId.HasValue)
            query = query.Where(l => l.TenantId == tenantId.Value);

        if (unitId.HasValue)
            query = query.Where(l => l.UnitId == unitId.Value);

        var inService = await query
            .Where(l => l.ConversationState == "service")
            .CountAsync();

        var inQueue = await query
            .Where(l => l.ConversationState == "queue")
            .CountAsync();

        var inBot = await query
            .Where(l => l.ConversationState == "bot")
            .CountAsync();

        var concluded = await query
            .Where(l => l.ConversationState == "concluido")
            .CountAsync();

        return new LeadsInServiceDto
        {
            InService = inService,
            InQueue = inQueue,
            InBot = inBot,
            Concluded = concluded,
            TotalActive = inService + inQueue + inBot
        };
    }

    public async Task<int> GetCheckClosedQueries(int tenantId, int? unitId = null)
    {
        var query = _db.Leads
            .AsNoTracking()
            .Where(l =>
                l.TenantId == tenantId &&
                (l.CurrentStage == "10_EM_TRATAMENTO" || l.CurrentStage == "09_FECHOU_TRATAMENTO"));

        if (unitId.HasValue)
            query = query.Where(l => l.UnitId == unitId.Value);

        return await query
            .Select(l => l.Id)
            .Distinct()
            .CountAsync();
    }

    public async Task<int> GetCheckStageWithoutPayment(int tenantId, int? unitId = null)
    {
        var query = _db.Leads
            .AsNoTracking()
            .Where(l =>
                l.TenantId == tenantId &&
                l.CurrentStage == "04_AGENDADO_SEM_PAGAMENTO");

        if (unitId.HasValue)
            query = query.Where(l => l.UnitId == unitId.Value);

        return await query.CountAsync();
    }

    public async Task<int> GetVerifyPaymentStep(int clinicId)
    {
        return await _db.Leads
            .AsNoTracking()
            .Where(l =>
                l.UnitId == clinicId &&
                l.CurrentStage == "05_AGENDADO_COM_PAGAMENTO")
            .CountAsync();
    }

    public async Task<List<object>> GetVerifySourceFinal(int clinicId)
    {
        return await _db.Leads
            .AsNoTracking()
            .Where(l => l.TenantId == clinicId)
            .GroupBy(l => l.Source)
            .Select(g => new
            {
                Origem = g.Key,
                Quantidade = g.Count()
            })
            .ToListAsync<object>();
    }


    public async Task<List<EtapaAgrupadaDto>> GetCheckGroupedStep(int clinicId)
    {
        return await _db.Leads
            .AsNoTracking()
            .Where(l => l.TenantId == clinicId)
            .GroupBy(l => string.IsNullOrWhiteSpace(l.CurrentStage)
                ? "SEM_ETAPA"
                : l.CurrentStage.Trim())
            .Select(g => new EtapaAgrupadaDto
            {
                Etapa = g.Key,
                Quantidade = g.Count()
            })
            .ToListAsync();
    }

    public async Task<IEnumerable<Lead>> GetWeekendLeads(int clinicId)
    {
        var brazilTz = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");

        var leads = await _db.Leads
            .AsNoTracking()
            .Where(l => l.TenantId == clinicId)
            .ToListAsync();

        return [.. leads.Where(l =>
        {
            var local = TimeZoneInfo.ConvertTimeFromUtc(l.CreatedAt, brazilTz);
            return local.DayOfWeek switch
            {
                DayOfWeek.Saturday => local.Hour >= 18,
                DayOfWeek.Sunday => true,
                DayOfWeek.Monday => local.Hour < 18,
                _ => false
            };
        })];
    }

    public async Task<IEnumerable<Lead>> GetCampaignLeads(int clinicId)
    {
        return await _db.Leads
            .AsNoTracking()
            .Where(l => l.TenantId == clinicId && l.Campaign != null && l.Campaign != "DESCONHECIDO")
            .ToListAsync();
    }

    public async Task<List<Lead>> GetLeadAds(int clinicId)
    {
        return await _db.Leads
            .AsNoTracking()
            .Where(l => l.TenantId == clinicId && l.Ad != null && l.Ad != "DESCONHECIDO")
            .ToListAsync();
    }

    public async Task<IEnumerable<LeadsMesDto>> GetSearchStartMonthLeads(int clinicId, DateTime dataInicio, DateTime finalData)
    {
        var tz = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");

        var dataInicioUtc = TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(dataInicio, DateTimeKind.Unspecified), tz);

        var dataFinalUtc = TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(finalData, DateTimeKind.Unspecified), tz).AddDays(1);

        var leads = await _db.Leads
            .AsNoTracking()
            .Where(l =>
                l.TenantId == clinicId &&
                l.CreatedAt >= dataInicioUtc &&
                l.CreatedAt < dataFinalUtc)
            .ToListAsync();

        return [.. leads
            .GroupBy(l => new { l.CreatedAt.Year, l.CreatedAt.Month })
            .Select(g => new LeadsMesDto
            {
                Ano = g.Key.Year,
                Mes = g.Key.Month,
                Quantidade = g.Count()
            })
            .OrderBy(x => x.Ano)
            .ThenBy(x => x.Mes)];
    }

    public async Task<IEnumerable<LeadsMesDto>> GetQueryLeadsByPeriodService(FiltroLeadsPeriodoDto filtro)
    {
        var leadsQuery = _db.Leads
            .AsNoTracking()
            .Where(l => l.TenantId == filtro.ClinicId);

        var tz = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");

        var inicioAnoLocal = new DateTime(filtro.Ano, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);
        var inicioAnoUtc = TimeZoneInfo.ConvertTimeToUtc(inicioAnoLocal, tz);
        var fimAnoUtc = TimeZoneInfo.ConvertTimeToUtc(inicioAnoLocal.AddYears(1), tz);

        leadsQuery = leadsQuery
            .Where(l => l.CreatedAt >= inicioAnoUtc && l.CreatedAt < fimAnoUtc);

        if (filtro.Mes.HasValue)
        {
            var inicioMesLocal = new DateTime(filtro.Ano, filtro.Mes.Value, 1, 0, 0, 0, DateTimeKind.Unspecified);
            var inicioMesUtc = TimeZoneInfo.ConvertTimeToUtc(inicioMesLocal, tz);
            var fimMesUtc = TimeZoneInfo.ConvertTimeToUtc(inicioMesLocal.AddMonths(1), tz);

            leadsQuery = leadsQuery
                .Where(l => l.CreatedAt >= inicioMesUtc && l.CreatedAt < fimMesUtc);
        }

        if (filtro.Dia.HasValue)
        {
            var inicioDiaLocal = new DateTime(
                filtro.Ano,
                filtro.Mes ?? 1,
                filtro.Dia.Value,
                0, 0, 0,
                DateTimeKind.Unspecified);

            var inicioDiaUtc = TimeZoneInfo.ConvertTimeToUtc(inicioDiaLocal, tz);
            var fimDiaUtc = TimeZoneInfo.ConvertTimeToUtc(inicioDiaLocal.AddDays(1), tz);

            leadsQuery = leadsQuery
                .Where(l => l.CreatedAt >= inicioDiaUtc && l.CreatedAt < fimDiaUtc);
        }

        var resultado = await leadsQuery
            .GroupBy(l => new { l.CreatedAt.Year, l.CreatedAt.Month })
            .Select(g => new LeadsMesDto
            {
                Ano = g.Key.Year,
                Mes = g.Key.Month,
                Quantidade = g.Count()
            })
            .OrderBy(x => x.Ano)
            .ThenBy(x => x.Mes)
            .ToListAsync();

        return resultado;
    }

    // ═══════════════════════════════════════════════════════════════
    // 🛠️ MÉTODOS AUXILIARES (sem alteração)
    // ═══════════════════════════════════════════════════════════════



    private static bool GetAppointmentAvailable(string? stage) =>
        LeadStages.HasAppointmentRecord(stage);


    /// <summary>
    /// Obter leads ativos (não concluídos) para sincronização com n8n
    /// </summary>
    /// <param name="limit">Limite de leads a retornar (padrão: 100)</param>
    /// <param name="unitId">Filtrar por unidade específica (opcional)</param>
    /// <returns>Lista de leads ativos com dados mínimos</returns>
    public async Task<List<ActiveLeadDto>> GetActiveLeadsAsync(int limit = 100, int? unitId = null)
    {
        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation(
                "📊 Buscando leads ativos (limite: {Limit}, unitId: {UnitId})", 
                limit, unitId);
        }

        // ─────────────────────────────────────────────────────────
        // BUSCAR LEADS ATIVOS (NÃO CONCLUÍDOS)
        // ─────────────────────────────────────────────────────────
        
        var query = _db.Leads
            .AsNoTracking()
            .Where(l => 
                l.ConversationState != null &&
                l.ConversationState != "concluido");

        // Filtrar por unidade se especificado
        if (unitId.HasValue)
        {
            query = query.Where(l => l.UnitId == unitId.Value);
        }

        var activeLeads = await query
            .OrderBy(l => l.UpdatedAt) // Priorizar leads mais antigos sem atualização
            .Take(limit)
            .Select(l => new ActiveLeadDto
            {
                Id = l.Id,
                ExternalId = l.ExternalId,
                Name = l.Name,
                Phone = l.Phone,
                ConversationState = l.ConversationState!,
                AttendantId = l.AttendantId,
                UnitId = l.UnitId,
                UpdatedAt = l.UpdatedAt,
                CreatedAt = l.CreatedAt
            })
            .ToListAsync();

        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation(
                "✅ Retornados {Count} leads ativos para sincronização", 
                activeLeads.Count);
        }

        return activeLeads;
    }

    /// <summary>
    /// Dados consolidados para a página de Evolução avançada:
    /// série mensal (com acumulado, MoM, média móvel), por dia-da-semana, por hora do dia,
    /// origens ao longo do tempo e conversão mês a mês.
    /// </summary>
    public async Task<EvolutionAdvancedDto> GetEvolutionAdvancedAsync(
        int? clinicId,
        DateTime dataInicio,
        DateTime dataFim)
    {
        var tz = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");

        var startLocal = DateTime.SpecifyKind(new DateTime(dataInicio.Year, dataInicio.Month, 1), DateTimeKind.Unspecified);
        var endLocal = DateTime.SpecifyKind(
            new DateTime(dataFim.Year, dataFim.Month, 1).AddMonths(1), DateTimeKind.Unspecified);

        var startUtc = TimeZoneInfo.ConvertTimeToUtc(startLocal, tz);
        var endUtc = TimeZoneInfo.ConvertTimeToUtc(endLocal, tz);

        var query = _db.Leads.AsNoTracking()
            .Where(l => l.CreatedAt >= startUtc && l.CreatedAt < endUtc);

        if (clinicId.HasValue && clinicId.Value > 0)
            query = query.Where(l => l.TenantId == clinicId.Value);

        var raw = await query
            .Select(l => new
            {
                l.CreatedAt,
                l.Source,
                l.CurrentStage,
                l.HasAppointment,
                l.HasPayment
            })
            .ToListAsync();

        var leads = raw.Select(l => new
        {
            Local = TimeZoneInfo.ConvertTimeFromUtc(
                DateTime.SpecifyKind(l.CreatedAt, DateTimeKind.Utc), tz),
            Source = string.IsNullOrWhiteSpace(l.Source) ? "DESCONHECIDO" : l.Source,
            l.CurrentStage,
            l.HasAppointment,
            l.HasPayment
        }).ToList();

        // ── Esqueleto de meses no intervalo ────────────────────────
        var monthKeys = new List<(int Year, int Month, string Label)>();
        var cur = startLocal;
        while (cur < endLocal)
        {
            monthKeys.Add((cur.Year, cur.Month, $"{MonthShortPt(cur.Month)}/{cur:yy}"));
            cur = cur.AddMonths(1);
        }

        // ── Série mensal ────────────────────────────────────────────
        var monthlyGroups = leads
            .GroupBy(l => new { l.Local.Year, l.Local.Month })
            .ToDictionary(g => (g.Key.Year, g.Key.Month), g => g.Count());

        var monthlyRaw = monthKeys
            .Select(k => new { k.Year, k.Month, k.Label, Total = monthlyGroups.GetValueOrDefault((k.Year, k.Month), 0) })
            .ToList();

        var monthly = new List<EvolutionMonthPointDto>();
        var acumulado = 0;
        for (var i = 0; i < monthlyRaw.Count; i++)
        {
            var m = monthlyRaw[i];
            acumulado += m.Total;

            double? mom = null;
            if (i > 0)
            {
                var prev = monthlyRaw[i - 1].Total;
                mom = prev == 0 ? (m.Total > 0 ? 100d : 0d) : ((m.Total - prev) * 100d / prev);
            }

            double? mm3 = null;
            if (i >= 2)
            {
                mm3 = (monthlyRaw[i].Total + monthlyRaw[i - 1].Total + monthlyRaw[i - 2].Total) / 3d;
            }

            monthly.Add(new EvolutionMonthPointDto
            {
                Year = m.Year,
                Month = m.Month,
                Label = m.Label,
                Total = m.Total,
                Cumulative = acumulado,
                MomGrowthPercent = mom,
                MovingAverage3 = mm3
            });
        }

        // ── Agregados estatísticos ──────────────────────────────────
        var totals = monthly.Select(m => m.Total).ToList();
        var totalLeads = totals.Sum();
        var avg = totals.Count > 0 ? totals.Average() : 0d;
        var median = Median(totals);
        var stddev = StdDev(totals, avg);

        var bestMonth = monthly.OrderByDescending(m => m.Total).FirstOrDefault();
        var worstMonth = monthly
            .Where(m => m.Total > 0)
            .OrderBy(m => m.Total)
            .FirstOrDefault() ?? monthly.LastOrDefault();

        double growthFirstToLast = 0;
        if (monthly.Count >= 2)
        {
            var first = monthly.First().Total;
            var last = monthly.Last().Total;
            growthFirstToLast = first == 0 ? (last > 0 ? 100d : 0d) : ((last - first) * 100d / first);
        }

        // ── Por dia da semana (seg=1 ... dom=7) ─────────────────────
        var weekdayLabels = new[] { "Seg", "Ter", "Qua", "Qui", "Sex", "Sáb", "Dom" };
        var weekdayGroup = leads
            .GroupBy(l => ((int)l.Local.DayOfWeek + 6) % 7) // 0=Seg, 6=Dom
            .ToDictionary(g => g.Key, g => g.Count());

        var weekday = Enumerable.Range(0, 7).Select(i => new EvolutionWeekdayDto
        {
            Weekday = i,
            Label = weekdayLabels[i],
            Total = weekdayGroup.GetValueOrDefault(i, 0)
        }).ToList();

        // ── Por hora do dia ─────────────────────────────────────────
        var hourGroup = leads
            .GroupBy(l => l.Local.Hour)
            .ToDictionary(g => g.Key, g => g.Count());

        var hour = Enumerable.Range(0, 24).Select(h => new EvolutionHourDto
        {
            Hour = h,
            Total = hourGroup.GetValueOrDefault(h, 0)
        }).ToList();

        // ── Origens ao longo do tempo (top 6) ───────────────────────
        var sourceTotals = leads
            .GroupBy(l => l.Source)
            .Select(g => new { Source = g.Key, Total = g.Count() })
            .OrderByDescending(x => x.Total)
            .ToList();

        var topSources = sourceTotals.Take(6).Select(s => s.Source).ToList();

        var sourceBuckets = leads
            .GroupBy(l => new
            {
                Source = topSources.Contains(l.Source) ? l.Source : "OUTROS",
                l.Local.Year,
                l.Local.Month
            })
            .Select(g => new { g.Key.Source, g.Key.Year, g.Key.Month, Count = g.Count() })
            .ToList();

        var sourceNames = topSources.ToList();
        if (sourceTotals.Count > 6) sourceNames.Add("OUTROS");

        var sourcesOverTime = sourceNames.Select(src =>
        {
            var serie = new EvolutionSourceSerieDto
            {
                Source = src,
                Total = sourceBuckets.Where(b => b.Source == src).Sum(b => b.Count),
                Points = monthKeys.Select(k =>
                {
                    var match = sourceBuckets.FirstOrDefault(b =>
                        b.Source == src && b.Year == k.Year && b.Month == k.Month);
                    return new EvolutionSourceMonthDto
                    {
                        Year = k.Year,
                        Month = k.Month,
                        Label = k.Label,
                        Count = match?.Count ?? 0
                    };
                }).ToList()
            };
            return serie;
        }).ToList();

        // ── Conversão mês a mês ─────────────────────────────────────
        var conversionOverTime = monthKeys.Select(k =>
        {
            var monthLeads = leads.Where(l => l.Local.Year == k.Year && l.Local.Month == k.Month).ToList();
            var total = monthLeads.Count;
            var agendado = monthLeads.Count(l => l.HasAppointment);
            var pago = monthLeads.Count(l => l.HasPayment);
            var tratamento = monthLeads.Count(l => l.CurrentStage == "09_FECHOU_TRATAMENTO"
                                                || l.CurrentStage == "10_EM_TRATAMENTO");

            return new EvolutionConversionPointDto
            {
                Year = k.Year,
                Month = k.Month,
                Label = k.Label,
                Total = total,
                Agendado = agendado,
                Pago = pago,
                Tratamento = tratamento,
                AgendadoRate = total == 0 ? 0 : agendado * 100d / total,
                PagoRate = total == 0 ? 0 : pago * 100d / total
            };
        }).ToList();

        return new EvolutionAdvancedDto
        {
            StartDateLocal = startLocal,
            EndDateLocal = endLocal.AddDays(-1),
            ClinicId = clinicId,
            TotalLeads = totalLeads,
            AverageMonthly = avg,
            MedianMonthly = median,
            StdDevMonthly = stddev,
            BestMonthTotal = bestMonth?.Total ?? 0,
            BestMonthLabel = bestMonth?.Label ?? "",
            WorstMonthTotal = worstMonth?.Total ?? 0,
            WorstMonthLabel = worstMonth?.Label ?? "",
            GrowthPercentFirstToLast = growthFirstToLast,
            Monthly = monthly,
            Weekday = weekday,
            Hour = hour,
            SourcesOverTime = sourcesOverTime,
            ConversionOverTime = conversionOverTime
        };
    }

    private static string MonthShortPt(int month) => month switch
    {
        1 => "jan", 2 => "fev", 3 => "mar", 4 => "abr",
        5 => "mai", 6 => "jun", 7 => "jul", 8 => "ago",
        9 => "set", 10 => "out", 11 => "nov", 12 => "dez",
        _ => "-"
    };

    private static double Median(List<int> values)
    {
        if (values.Count == 0) return 0;
        var sorted = values.OrderBy(v => v).ToList();
        var mid = sorted.Count / 2;
        return sorted.Count % 2 == 0
            ? (sorted[mid - 1] + sorted[mid]) / 2d
            : sorted[mid];
    }

    private static double StdDev(List<int> values, double mean)
    {
        if (values.Count <= 1) return 0;
        var sumSq = values.Sum(v => (v - mean) * (v - mean));
        return Math.Sqrt(sumSq / values.Count);
    }

    /// <summary>
    /// Leads recebidos na madrugada (20h → 07h do dia seguinte) para a unidade informada.
    /// Caso nenhum clinicId seja informado, o padrão é 8020 (Araguaína).
    /// </summary>
    public async Task<OvernightLeadsDto> GetOvernightLeadsAsync(
        int? clinicId = null,
        DateTime? referenceDateLocal = null,
        int startHour = 20,
        int endHour = 7)
    {
        var targetClinic = clinicId ?? 8020;

        var tz = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");
        var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
        var refLocal = referenceDateLocal ?? nowLocal;

        var endLocal = new DateTime(refLocal.Year, refLocal.Month, refLocal.Day, endHour, 0, 0, DateTimeKind.Unspecified);
        var startLocal = endLocal.AddDays(-1).Date.AddHours(startHour);

        var startUtc = TimeZoneInfo.ConvertTimeToUtc(startLocal, tz);
        var endUtc = TimeZoneInfo.ConvertTimeToUtc(endLocal, tz);

        var unit = await _db.Units
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.ClinicId == targetClinic);

        var query = _db.Leads
            .AsNoTracking()
            .Where(l => l.TenantId == targetClinic
                     && l.CreatedAt >= startUtc
                     && l.CreatedAt < endUtc);

        var rawLeads = await query
            .OrderByDescending(l => l.CreatedAt)
            .Select(l => new
            {
                l.Id,
                l.Name,
                l.Phone,
                l.Source,
                l.Channel,
                l.CurrentStage,
                l.ConversationState,
                l.CreatedAt
            })
            .ToListAsync();

        var leads = rawLeads.Select(l => new OvernightLeadItemDto
        {
            Id = l.Id,
            Name = l.Name,
            Phone = l.Phone == "AGUARDANDO_COLETA" ? null : l.Phone,
            Source = l.Source,
            Channel = l.Channel,
            CurrentStage = l.CurrentStage,
            ConversationState = l.ConversationState,
            CreatedAt = l.CreatedAt,
            CreatedAtLocal = TimeZoneInfo.ConvertTimeFromUtc(
                DateTime.SpecifyKind(l.CreatedAt, DateTimeKind.Utc), tz)
        }).ToList();

        var hourBreakdown = leads
            .GroupBy(l => l.CreatedAtLocal.Hour)
            .Select(g => new OvernightHourBucketDto { Hour = g.Key, Count = g.Count() })
            .OrderBy(x => x.Hour)
            .ToList();

        var sourceBreakdown = leads
            .GroupBy(l => string.IsNullOrWhiteSpace(l.Source) ? "DESCONHECIDO" : l.Source)
            .Select(g => new OvernightSourceBucketDto { Source = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .ToList();

        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation(
                "🌙 Overnight leads {ClinicId} | {Start} → {End} | total={Total}",
                targetClinic, startLocal, endLocal, leads.Count);
        }

        return new OvernightLeadsDto
        {
            Total = leads.Count,
            ClinicId = targetClinic,
            UnitId = unit?.Id,
            UnitName = unit?.Name ?? $"Unidade {targetClinic}",
            PeriodStartLocal = startLocal,
            PeriodEndLocal = endLocal,
            StartHour = startHour,
            EndHour = endHour,
            Leads = leads,
            HourBreakdown = hourBreakdown,
            SourceBreakdown = sourceBreakdown
        };
    }

    /// <summary>
    /// Obter contagem de leads por estado
    /// </summary>
    public async Task<Dictionary<string, int>> GetLeadsCountByStateAsync(int? unitId = null)
    {
        var query = _db.Leads.AsNoTracking();

        if (unitId.HasValue)
        {
            query = query.Where(l => l.UnitId == unitId.Value);
        }

        var counts = await query
            .Where(l => l.ConversationState != null)
            .GroupBy(l => l.ConversationState)
            .Select(g => new { State = g.Key!, Count = g.Count() })
            .ToDictionaryAsync(x => x.State, x => x.Count);

        return counts;
    }

    // ════════════════════════════════════════════════════════════════
    //  Drill-downs do dashboard (consultas agendadas / compareceram)
    // ════════════════════════════════════════════════════════════════

    public async Task<List<DashboardLeadListItemDto>> GetScheduledLeadsAsync(
        int clinicId,
        DateTime dateFrom,
        DateTime dateTo,
        int? unitId,
        int? attendantId,
        string? source,
        CancellationToken ct = default)
    {
        var startUtc = DateTime.SpecifyKind(dateFrom.Date, DateTimeKind.Utc);
        var endExclUtc = DateTime.SpecifyKind(dateTo.Date.AddDays(1), DateTimeKind.Utc);

        var q = _db.Leads.AsNoTracking()
            .Include(l => l.Unit)
            .Include(l => l.Attendant)
            .Where(l => l.TenantId == clinicId
                     && l.CreatedAt >= startUtc
                     && l.CreatedAt < endExclUtc
                     && (l.CurrentStage == LeadStages.AgendadoSemPagamento
                      || l.CurrentStage == LeadStages.AgendadoComPagamento
                      || l.CurrentStage == LeadStages.Faltou
                      || l.CurrentStage == LeadStages.NaoFechouTratamento
                      || l.CurrentStage == LeadStages.FechouTratamento
                      || l.CurrentStage == LeadStages.EmTratamento));

        if (unitId.HasValue) q = q.Where(l => l.UnitId == unitId.Value);
        if (attendantId.HasValue) q = q.Where(l => l.AttendantId == attendantId.Value);
        if (!string.IsNullOrWhiteSpace(source)) q = q.Where(l => l.Source == source);

        return await q
            .OrderByDescending(l => l.AppointmentScheduledAt ?? l.CreatedAt)
            .Select(l => new DashboardLeadListItemDto
            {
                Id = l.Id,
                Name = l.Name,
                Phone = l.Phone == "AGUARDANDO_COLETA" ? null : l.Phone,
                UnitId = l.UnitId,
                UnitName = l.Unit != null ? l.Unit.Name : null,
                AttendantId = l.AttendantId,
                AttendantName = l.Attendant != null ? l.Attendant.Name : null,
                Source = l.Source,
                Campaign = l.Campaign,
                CurrentStage = l.CurrentStage,
                AttendanceStatus = l.AttendanceStatus,
                AppointmentScheduledAt = l.AppointmentScheduledAt,
                AttendanceStatusAt = l.AttendanceStatusAt,
                CreatedAt = l.CreatedAt,
                ConsultationValue = l.ConsultationValue,
                ClosedTreatment = l.ClosedTreatment,
            })
            .ToListAsync(ct);
    }

    public async Task<List<DashboardLeadListItemDto>> GetAttendedLeadsAsync(
        int clinicId,
        DateTime dateFrom,
        DateTime dateTo,
        int? unitId,
        int? attendantId,
        string? source,
        CancellationToken ct = default)
    {
        var startUtc = DateTime.SpecifyKind(dateFrom.Date, DateTimeKind.Utc);
        var endExclUtc = DateTime.SpecifyKind(dateTo.Date.AddDays(1), DateTimeKind.Utc);

        var q = _db.Leads.AsNoTracking()
            .Include(l => l.Unit)
            .Include(l => l.Attendant)
            .Where(l => l.TenantId == clinicId
                     && l.CreatedAt >= startUtc
                     && l.CreatedAt < endExclUtc
                     && l.AttendanceStatus == LeadStages.AttendedCompareceu);

        if (unitId.HasValue) q = q.Where(l => l.UnitId == unitId.Value);
        if (attendantId.HasValue) q = q.Where(l => l.AttendantId == attendantId.Value);
        if (!string.IsNullOrWhiteSpace(source)) q = q.Where(l => l.Source == source);

        return await q
            .OrderByDescending(l => l.AttendanceStatusAt ?? l.UpdatedAt)
            .Select(l => new DashboardLeadListItemDto
            {
                Id = l.Id,
                Name = l.Name,
                Phone = l.Phone == "AGUARDANDO_COLETA" ? null : l.Phone,
                UnitId = l.UnitId,
                UnitName = l.Unit != null ? l.Unit.Name : null,
                AttendantId = l.AttendantId,
                AttendantName = l.Attendant != null ? l.Attendant.Name : null,
                Source = l.Source,
                Campaign = l.Campaign,
                CurrentStage = l.CurrentStage,
                AttendanceStatus = l.AttendanceStatus,
                AppointmentScheduledAt = l.AppointmentScheduledAt,
                AttendanceStatusAt = l.AttendanceStatusAt,
                CreatedAt = l.CreatedAt,
                ConsultationValue = l.ConsultationValue,
                ClosedTreatment = l.ClosedTreatment,
            })
            .ToListAsync(ct);
    }

    // ════════════════════════════════════════════════════════════════
    //  Dashboard overview — tudo que o dashboard precisa num request,
    //  filtrado por dateFrom/dateTo em Lead.CreatedAt.
    // ════════════════════════════════════════════════════════════════

    // Normaliza para UTC respeitando o Kind (mesma regra do KpiConfigService),
    // para que o overview e o breakdown janelem o período de forma idêntica.
    private static DateTime AsUtc(DateTime d) =>
        d.Kind == DateTimeKind.Utc ? d
        : d.Kind == DateTimeKind.Local ? d.ToUniversalTime()
        : DateTime.SpecifyKind(d, DateTimeKind.Utc);

    public async Task<DashboardOverviewDto> GetDashboardOverviewAsync(
        int clinicId,
        DateTime dateFrom,
        DateTime dateTo,
        int? unitId,
        int? attendantId,
        string? source,
        string? responsibleUser = null,
        CancellationToken ct = default)
    {
        if (dateTo < dateFrom) throw new ArgumentException("dateTo deve ser >= dateFrom");

        // Janela alinhada ao INSTANTE exato enviado pelo front (a meia-noite/fim-de-dia
        // do fuso local já vem embutida no ISO, ex.: 00:00 BRT = 03:00Z). NÃO truncar
        // para .Date — isso reintroduzia a meia-noite UTC e desalinhava o filtro "Dia"
        // em ~3h (leads da noite caíam no dia UTC seguinte e sumiam do "Dia").
        var startUtc = AsUtc(dateFrom);
        var endUtc = AsUtc(dateTo);
        // Se vier só a data (meia-noite), inclui o dia inteiro; se vier instante de
        // fim-de-dia (como o front manda), usa o próprio instante como fim exclusivo.
        var endExclUtc = endUtc.TimeOfDay == TimeSpan.Zero ? endUtc.AddDays(1) : endUtc;

        // scopeQ = filtros de lead SEM a janela de data — reaproveitado para contar
        // agendados pela data de ENTRADA na etapa (e não por criação).
        // ExcludeDeleted: tira leads que foram deletados na Kommo (webhook delete só
        // marca Status="deleted", não apaga a linha). Sem isso, divergia com a Kommo.
        var scopeQ = _db.Leads.AsNoTracking().Where(l => l.TenantId == clinicId).ExcludeDeleted();
        if (unitId.HasValue) scopeQ = scopeQ.Where(l => l.UnitId == unitId.Value);
        if (attendantId.HasValue) scopeQ = scopeQ.Where(l => l.AttendantId == attendantId.Value);
        if (!string.IsNullOrWhiteSpace(source)) scopeQ = scopeQ.Where(l => l.Source == source);

        // Filtro por SDR responsável (custom field "Usuário responsável"). Aplicado por
        // último para que todos os KPIs/agregações abaixo já considerem só os leads dele.
        scopeQ = await ResponsibleUserFilter.ApplyAsync(scopeQ, responsibleUser, ct);

        // baseQ = scopeQ + janela pela DATA REAL DE CRIAÇÃO do lead. Prefere OriginalCreatedAt
        // (vindo da Kommo via custom field "Data de criação lead" / backfill da Cloudia) e cai
        // pra CreatedAt do nosso backend só quando ela não existe.
        var baseQ = scopeQ.Where(l =>
            (l.OriginalCreatedAt ?? l.CreatedAt) >= startUtc &&
            (l.OriginalCreatedAt ?? l.CreatedAt) <  endExclUtc);

        var totalLeads = await baseQ.CountAsync(ct);

        // Leads que foram deletados na Kommo dentro do período (webhook delete →
        // Status="deleted"). NÃO entram em totalLeads (scopeQ já exclui via
        // ExcludeDeleted), mas o front mostra como chip "Deletados" pra SDR entender.
        var totalLeadsDeleted = await _db.Leads.AsNoTracking()
            .Where(l => l.TenantId == clinicId
                     && l.Status == LeadQueryExtensions.StatusDeleted
                     && (l.OriginalCreatedAt ?? l.CreatedAt) >= startUtc
                     && (l.OriginalCreatedAt ?? l.CreatedAt) <  endExclUtc
                     && (!unitId.HasValue || l.UnitId == unitId.Value))
            .CountAsync(ct);

        // KPIs por etapa
        // Consultas: leads com "Data de agendamento" (Lead.AppointmentScheduledAt) DENTRO
        // do range. Não por stage — inclui leads agendados pra futuro no período E os
        // que já compareceram. Sai do baseQ (cohort por criação) e vai pelo scopeQ porque
        // a janela aqui é a do agendamento, não a da criação do lead.
        var consultas = await scopeQ
            .Where(l => l.AppointmentScheduledAt != null
                     && l.AppointmentScheduledAt >= startUtc
                     && l.AppointmentScheduledAt <  endExclUtc)
            .CountAsync(ct);
        var comPag = await baseQ
            .Where(l => l.CurrentStage == LeadStages.AgendadoComPagamento)
            .CountAsync(ct);
        var semPag = await baseQ
            .Where(l => l.CurrentStage == LeadStages.AgendadoSemPagamento)
            .CountAsync(ct);

        // KPIs de comparecimento / fechamento
        var consultasAgendadas = await baseQ
            .Where(l => l.CurrentStage == LeadStages.AgendadoSemPagamento
                     || l.CurrentStage == LeadStages.AgendadoComPagamento
                     || l.CurrentStage == LeadStages.Faltou
                     || l.CurrentStage == LeadStages.NaoFechouTratamento
                     || l.CurrentStage == LeadStages.FechouTratamento
                     || l.CurrentStage == LeadStages.EmTratamento)
            .CountAsync(ct);
        var compareceu = await baseQ
            .Where(l => l.AttendanceStatus == LeadStages.AttendedCompareceu)
            .CountAsync(ct);
        var faltou = await baseQ
            .Where(l => l.CurrentStage == LeadStages.Faltou
                     || l.AttendanceStatus == LeadStages.AttendedFaltou)
            .CountAsync(ct);
        var naoFechou = await baseQ
            .Where(l => l.CurrentStage == LeadStages.NaoFechouTratamento)
            .CountAsync(ct);
        var fechou = await baseQ
            .Where(l => l.CurrentStage == LeadStages.FechouTratamento
                     || l.CurrentStage == LeadStages.EmTratamento)
            .CountAsync(ct);

        // Leads ativos: tudo que ainda está no funil (não chegou a estado terminal:
        // ganho/perda/faltou). Inclui leads em "entrada", "em atendimento", agendados etc.
        var leadsAtivos = await baseQ
            .Where(l => l.CurrentStage != LeadStages.FechouTratamento
                     && l.CurrentStage != LeadStages.NaoFechouTratamento
                     && l.CurrentStage != LeadStages.Faltou)
            .CountAsync(ct);

        // Estados da conversa (bot/queue/service/concluido)
        var stateRows = await baseQ
            .GroupBy(l => l.ConversationState ?? "")
            .Select(g => new { State = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        var states = new LeadsCountDto
        {
            Bot = stateRows.FirstOrDefault(s => s.State == "bot")?.Count ?? 0,
            Queue = stateRows.FirstOrDefault(s => s.State == "queue")?.Count ?? 0,
            Service = stateRows.FirstOrDefault(s => s.State == "service")?.Count ?? 0,
            Concluido = stateRows.FirstOrDefault(s => s.State == "concluido")?.Count ?? 0,
        };
        states.Total = states.Bot + states.Queue + states.Service + states.Concluido;

        // Etapas agrupadas
        var etapas = await baseQ
            .GroupBy(l => string.IsNullOrWhiteSpace(l.CurrentStage) ? "SEM_ETAPA" : l.CurrentStage.Trim())
            .Select(g => new EtapaAgrupadaDto { Etapa = g.Key, Quantidade = g.Count() })
            .OrderByDescending(e => e.Quantidade)
            .ToListAsync(ct);

        // Origens agrupadas
        var origens = await baseQ
            .GroupBy(l => l.Source)
            .Select(g => new OrigemAgrupadaDto { Origem = g.Key, Quantidade = g.Count() })
            .OrderByDescending(o => o.Quantidade)
            .ToListAsync(ct);

        // Origens das consultas (leads agendados+)
        var origensConsultas = await baseQ
            .Where(l => l.CurrentStage == LeadStages.AgendadoSemPagamento
                     || l.CurrentStage == LeadStages.AgendadoComPagamento
                     || l.CurrentStage == LeadStages.Faltou
                     || l.CurrentStage == LeadStages.NaoFechouTratamento
                     || l.CurrentStage == LeadStages.FechouTratamento
                     || l.CurrentStage == LeadStages.EmTratamento)
            .GroupBy(l => l.Source)
            .Select(g => new OrigemAgrupadaDto { Origem = g.Key, Quantidade = g.Count() })
            .OrderByDescending(o => o.Quantidade)
            .ToListAsync(ct);

        // Origens dos tratamentos fechados
        var origensTratamentos = await baseQ
            .Where(l => l.CurrentStage == LeadStages.FechouTratamento)
            .GroupBy(l => l.Source)
            .Select(g => new OrigemAgrupadaDto { Origem = g.Key, Quantidade = g.Count() })
            .OrderByDescending(o => o.Quantidade)
            .ToListAsync(ct);

        // ─── Funnel por tipo de base (geral/cadastro/resgate) ───────
        // Uma única ida ao banco agrupando por LeadType e contando cada etapa.
        var funnelRows = await baseQ
            .GroupBy(l => l.LeadType ?? "indefinido")
            .Select(g => new
            {
                Type = g.Key,
                Total = g.Count(),
                Interacoes = g.Count(l => l.HadInteraction == true),
                Agendados = g.Count(l => l.CurrentStage == LeadStages.AgendadoSemPagamento
                                      || l.CurrentStage == LeadStages.AgendadoComPagamento),
                Consultas = g.Count(l => l.CurrentStage == LeadStages.EmTratamento
                                      || l.CurrentStage == LeadStages.FechouTratamento
                                      || l.CurrentStage == LeadStages.NaoFechouTratamento),
                Tratamentos = g.Count(l => l.CurrentStage == LeadStages.FechouTratamento),
                NoShow = g.Count(l => l.CurrentStage == LeadStages.Faltou
                                    || l.AttendanceStatus == LeadStages.AttendedFaltou),
            })
            .ToListAsync(ct);

        // Agendados por DATA DE ENTRADA na etapa (histórico), e não por data de criação
        // do lead — inclui leads antigos agendados dentro do período. Mesmos filtros
        // (scopeQ). Sobrescreve a contagem cohort-by-creation do funil logo abaixo.
        var agStages = new[] { LeadStages.AgendadoSemPagamento, LeadStages.AgendadoComPagamento };
        // EntrySource != legacy: linhas legadas têm ChangedAt = updated_at (não é a data de
        // entrada na etapa) — eram a causa do "211 no dia". Contamos só webhook/eventos.
        // Dedup por LeadId (não por {Id,Type}): um lead que reentra ou cujo Type mudou conta uma vez.
        // kpi_exclusions: leads marcados como "não contar" pelo admin no drill-down.
        var agExcluded = await _db.KpiExclusions.AsNoTracking()
            .Where(e => e.TenantId == clinicId && e.KpiKey == "agendados"
                     && (!unitId.HasValue || e.UnitId == unitId.Value))
            .Select(e => e.LeadId)
            .ToListAsync(ct);
        var agEntryRows = await _db.LeadStageHistories.AsNoTracking()
            .Where(h => agStages.Contains(h.StageLabel)
                     && h.EntrySource != LeadStageHistory.SourceLegacy
                     && h.ChangedAt >= startUtc && h.ChangedAt < endExclUtc
                     && !agExcluded.Contains(h.LeadId))
            .Join(scopeQ, h => h.LeadId, l => l.Id, (h, l) => new { l.Id, Type = l.LeadType ?? "indefinido" })
            .ToListAsync(ct);
        var agByType = agEntryRows
            .GroupBy(x => x.Id)
            .Select(g => g.First().Type)
            .GroupBy(t => t)
            .ToDictionary(g => g.Key, g => g.Count());
        var agendadosTotal = agByType.Values.Sum();

        // Resgate por DATA DO PREENCHIMENTO de "Tentativas de resgastes" (recovery_attempts via
        // backfill de eventos) — NÃO por LeadType (vem vazio) nem por criação do lead. Conta lead
        // distinto na janela. Sobrescreve o total do funil de resgate (era a causa do "10").
        var resgateTotal = await _db.RecoveryAttempts.AsNoTracking()
            .Where(r => (r.EntrySource == "events_api" || r.EntrySource == "webhook")
                     && r.CreatedAt >= startUtc && r.CreatedAt < endExclUtc)
            .Join(scopeQ, r => r.LeadId, l => l.Id, (r, l) => r.LeadId)
            .Distinct()
            .CountAsync(ct);

        // Consultas por LeadType — mesma semântica do headline (por AppointmentScheduledAt
        // no range), agrupada pra dividir entre Cadastro/Resgate no funil.
        var consultasByType = await scopeQ
            .Where(l => l.AppointmentScheduledAt != null
                     && l.AppointmentScheduledAt >= startUtc
                     && l.AppointmentScheduledAt <  endExclUtc)
            .GroupBy(l => l.LeadType ?? "indefinido")
            .Select(g => new { Type = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        var consultasByTypeMap = consultasByType.ToDictionary(x => x.Type, x => x.Count);

        // Cadastro: NÃO conta por LeadType literal "cadastro" (perde os null/vazios, que
        // são a maioria em unidades onde o LeadType raramente é preenchido — ex.: Açailândia
        // tem 3935/3935 leads com LeadType=null). Conta IsCadastro(LeadType) = null/vazio ou
        // contém "cadastro"/"novo" — alinhado com KpiBreakdownsAsync (KpiConfigService:424).
        // Resgate roda em outra janela (recovery_attempts.CreatedAt), os dois não competem.
        var cadastroTotal = await baseQ
            .Where(l => l.LeadType == null
                     || l.LeadType == string.Empty
                     || EF.Functions.ILike(l.LeadType, "%cadastro%")
                     || EF.Functions.ILike(l.LeadType, "%novo%"))
            .CountAsync(ct);

        // No-show por DATA DE ENTRADA no stage (mesmo padrão de Agendados). Antes contava
        // l.CurrentStage == Faltou dentro de baseQ (filtrado por OriginalCreatedAt), o que
        // misturava "leads criados no período que ATUALMENTE estão em Faltou" — KPI virava
        // cumulativo do mês em vez de "entrou em Faltou no dia". Sobrescreve a contagem
        // cohort-by-creation do funil.
        var nsEntryRows = await _db.LeadStageHistories.AsNoTracking()
            .Where(h => h.StageLabel == LeadStages.Faltou
                     && h.EntrySource != LeadStageHistory.SourceLegacy
                     && h.ChangedAt >= startUtc && h.ChangedAt < endExclUtc)
            .Join(scopeQ, h => h.LeadId, l => l.Id, (h, l) => new { l.Id, Type = l.LeadType ?? "indefinido" })
            .ToListAsync(ct);
        var nsByType = nsEntryRows
            .GroupBy(x => x.Id)
            .Select(g => g.First().Type)
            .GroupBy(t => t)
            .ToDictionary(g => g.Key, g => g.Count());
        var noShowTotal = nsByType.Values.Sum();

        FunnelGroupDto BuildFunnel(string? type)
        {
            if (type == null)
            {
                return new FunnelGroupDto
                {
                    Total = funnelRows.Sum(r => r.Total),
                    Interacoes = funnelRows.Sum(r => r.Interacoes),
                    Agendados = agendadosTotal,
                    Consultas = funnelRows.Sum(r => r.Consultas),
                    Tratamentos = funnelRows.Sum(r => r.Tratamentos),
                    NoShow = noShowTotal,
                };
            }
            var row = funnelRows.FirstOrDefault(r => r.Type == type);
            return new FunnelGroupDto
            {
                // Resgate: total pela data real do preenchimento (recovery_attempts).
                // Cadastro: IsCadastro(LeadType) — não exige LeadType="cadastro" literal.
                Total = type switch
                {
                    "resgate" => resgateTotal,
                    "cadastro" => cadastroTotal,
                    _ => row?.Total ?? 0,
                },
                Interacoes = row?.Interacoes ?? 0,
                Agendados = agByType.GetValueOrDefault(type, 0),
                // Consultas: por Data de agendamento dentro do range, NÃO por stage.
                // Dependendo do LeadType "indefinido" o sistema mapeia pra cadastro/resgate
                // — mesmo critério do total: somamos o bucket "indefinido" no Cadastro.
                Consultas = type == "cadastro"
                    ? consultasByTypeMap.GetValueOrDefault("cadastro", 0) + consultasByTypeMap.GetValueOrDefault("indefinido", 0)
                    : consultasByTypeMap.GetValueOrDefault(type, 0),
                Tratamentos = row?.Tratamentos ?? 0,
                NoShow = nsByType.GetValueOrDefault(type, 0),
            };
        }

        // ─── Séries temporais ─────────────────────────────────────
        // Projeção leve pra agregar em memória (evita necessidade de funções de data
        // específicas do provider). N tipicamente < 50k por período no caso de uso.
        // CreatedAt = COALESCE(OriginalCreatedAt, CreatedAt) — mesma data real usada no filtro.
        var dateStageRows = await baseQ
            .Select(l => new { CreatedAt = (l.OriginalCreatedAt ?? l.CreatedAt), l.CurrentStage })
            .ToListAsync(ct);

        static string WeekKey(DateTime d)
        {
            var week = System.Globalization.ISOWeek.GetWeekOfYear(d);
            var year = System.Globalization.ISOWeek.GetYear(d);
            return $"{year}-W{week:D2}";
        }

        // "Dia comercial": o dia da clínica vai das 19h às 19h (lead após 19h conta como o
        // dia seguinte). CreatedAt é UTC; BRT = UTC-3 e o corte é 19h, então o relógio
        // comercial = UTC+2h (= BRT + 5h). Usado nas agregações por dia/semana.
        static DateTime BizClock(DateTime utc) => utc.AddHours(2);

        var leadsPorSemana = dateStageRows
            .GroupBy(r => WeekKey(BizClock(r.CreatedAt)))
            .OrderBy(g => g.Key)
            .Select(g => new PeriodoQtdDto { Periodo = g.Key, Quantidade = g.Count() })
            .ToList();

        var consultasPorSemana = dateStageRows
            .Where(r => r.CurrentStage == LeadStages.EmTratamento
                     || r.CurrentStage == LeadStages.FechouTratamento
                     || r.CurrentStage == LeadStages.NaoFechouTratamento)
            .GroupBy(r => WeekKey(BizClock(r.CreatedAt)))
            .OrderBy(g => g.Key)
            .Select(g => new PeriodoQtdDto { Periodo = g.Key, Quantidade = g.Count() })
            .ToList();

        var tratamentosPorSemana = dateStageRows
            .Where(r => r.CurrentStage == LeadStages.FechouTratamento)
            .GroupBy(r => WeekKey(BizClock(r.CreatedAt)))
            .OrderBy(g => g.Key)
            .Select(g => new PeriodoQtdDto { Periodo = g.Key, Quantidade = g.Count() })
            .ToList();

        // Dia da semana: .NET DayOfWeek = 0(Dom)..6(Sab) → mapear para 1..7. Pelo dia comercial.
        var dowCounts = dateStageRows
            .GroupBy(r => (int)BizClock(r.CreatedAt).DayOfWeek)
            .ToDictionary(g => g.Key, g => g.Count());
        var leadsPorDiaSemana = Enumerable.Range(0, 7)
            .Select(d => new DiaSemanaQtdDto
            {
                Dia = d + 1,
                Quantidade = dowCounts.TryGetValue(d, out var q) ? q : 0,
            })
            .ToList();

        var conversaoRate = totalLeads > 0 ? consultas * 100.0 / totalLeads : 0;
        var pagamentoRate = totalLeads > 0 ? comPag * 100.0 / totalLeads : 0;
        var semPagRate = totalLeads > 0 ? semPag * 100.0 / totalLeads : 0;
        // Taxa de comparecimento: compareceu / consultas agendadas (universo: leads que tiveram consulta).
        var comparecimentoRate = consultasAgendadas > 0 ? compareceu * 100.0 / consultasAgendadas : 0;
        // Taxa de fechamento: fechou / compareceu (mede o pós-consulta).
        var fechamentoRate = compareceu > 0 ? fechou * 100.0 / compareceu : 0;

        return new DashboardOverviewDto
        {
            DateFrom = dateFrom.Date,
            DateTo = dateTo.Date,
            TotalLeads = totalLeads,
            TotalLeadsDeleted = totalLeadsDeleted,
            Consultas = consultas,
            ComPagamento = comPag,
            SemPagamento = semPag,
            ConversaoRate = Math.Round(conversaoRate, 2),
            PagamentoRate = Math.Round(pagamentoRate, 2),
            SemPagamentoRate = Math.Round(semPagRate, 2),
            ConsultasAgendadas = consultasAgendadas,
            Compareceu = compareceu,
            Faltou = faltou,
            NaoFechou = naoFechou,
            Fechou = fechou,
            LeadsAtivos = leadsAtivos,
            ComparecimentoRate = Math.Round(comparecimentoRate, 2),
            FechamentoRate = Math.Round(fechamentoRate, 2),
            States = states,
            Etapas = etapas,
            Origens = origens,
            OrigensConsultas = origensConsultas,
            OrigensTratamentos = origensTratamentos,
            FunnelLeads = BuildFunnel(null),
            FunnelCadastro = BuildFunnel("cadastro"),
            FunnelResgate = BuildFunnel("resgate"),
            LeadsPorSemana = leadsPorSemana,
            ConsultasPorSemana = consultasPorSemana,
            TratamentosPorSemana = tratamentosPorSemana,
            LeadsPorDiaSemana = leadsPorDiaSemana,
        };
    }

    // ════════════════════════════════════════════════════════════════
    //  Leads recentes (notificações + página /recent-leads)
    // ════════════════════════════════════════════════════════════════

    public async Task<RecentLeadsResponseDto> GetRecentLeadsAsync(
        int clinicId,
        int hours,
        int limit,
        int? unitId,
        CancellationToken ct = default)
    {
        hours = Math.Clamp(hours, 1, 720);  // 1h a 30 dias
        limit = Math.Clamp(limit, 1, 200);

        var since = DateTime.UtcNow.AddHours(-hours);

        var q = _db.Leads.AsNoTracking()
            .Where(l => l.TenantId == clinicId && l.CreatedAt >= since);

        if (unitId.HasValue) q = q.Where(l => l.UnitId == unitId.Value);

        var total = await q.CountAsync(ct);

        var items = await q
            .OrderByDescending(l => l.CreatedAt)
            .Take(limit)
            .Select(l => new RecentLeadDto
            {
                Id = l.Id,
                ExternalId = l.ExternalId,
                Name = l.Name,
                Phone = l.Phone,
                Source = l.Source,
                Channel = l.Channel,
                CurrentStage = l.CurrentStage,
                ConversationState = l.ConversationState,
                UnitId = l.UnitId,
                UnitName = l.Unit != null ? l.Unit.Name : null,
                CreatedAt = l.CreatedAt,
            })
            .ToListAsync(ct);

        return new RecentLeadsResponseDto
        {
            Hours = hours,
            Total = total,
            Since = since,
            Items = items,
        };
    }

    // ════════════════════════════════════════════════════════════════
    //  Evolução por período com granularidade e comparação
    //  (alimenta o card "Evolução" + filtros do dashboard)
    // ════════════════════════════════════════════════════════════════

    public enum Granularity { Day, Week, Month, Quarter }
    public enum CompareMode { None, PreviousPeriod, PreviousYear }

    public async Task<DashboardEvolutionDto> GetEvolutionRangeAsync(
        int clinicId,
        DateTime dateFrom,
        DateTime dateTo,
        Granularity groupBy,
        CompareMode compare,
        int? unitId,
        int? attendantId,
        string? source,
        CancellationToken ct = default)
    {
        if (dateTo < dateFrom) throw new ArgumentException("dateTo deve ser >= dateFrom");

        // Normaliza pro início/fim do dia em UTC (+1 dia no fim inclusivo)
        var startUtc = DateTime.SpecifyKind(dateFrom.Date, DateTimeKind.Utc);
        var endExclUtc = DateTime.SpecifyKind(dateTo.Date.AddDays(1), DateTimeKind.Utc);

        var currentPoints = await BucketSeriesAsync(clinicId, startUtc, endExclUtc, groupBy, unitId, attendantId, source, ct);

        List<DashboardEvolutionPointDto>? comparePoints = null;
        DateTime? compareStart = null;
        DateTime? compareEnd = null;

        if (compare != CompareMode.None)
        {
            var spanDays = (endExclUtc - startUtc).TotalDays;

            (DateTime cStart, DateTime cEnd) = compare switch
            {
                CompareMode.PreviousPeriod => (startUtc.AddDays(-spanDays), endExclUtc.AddDays(-spanDays)),
                CompareMode.PreviousYear => (startUtc.AddYears(-1), endExclUtc.AddYears(-1)),
                _ => (startUtc, endExclUtc)
            };

            comparePoints = await BucketSeriesAsync(clinicId, cStart, cEnd, groupBy, unitId, attendantId, source, ct);
            compareStart = cStart;
            compareEnd = cEnd.AddDays(-1);
        }

        var totalCurrent = currentPoints.Sum(p => p.Count);
        var totalCompare = comparePoints?.Sum(p => p.Count) ?? 0;

        return new DashboardEvolutionDto
        {
            DateFrom = dateFrom.Date,
            DateTo = dateTo.Date,
            GroupBy = groupBy.ToString().ToLowerInvariant(),
            Compare = compare.ToString().ToLowerInvariant(),
            TotalCurrent = totalCurrent,
            TotalCompare = totalCompare,
            ChangePercent = totalCompare > 0
                ? Math.Round((double)(totalCurrent - totalCompare) / totalCompare * 100, 2)
                : (double?)null,
            Current = currentPoints,
            Comparison = comparePoints,
            ComparisonDateFrom = compareStart?.Date,
            ComparisonDateTo = compareEnd?.Date,
        };
    }

    private async Task<List<DashboardEvolutionPointDto>> BucketSeriesAsync(
        int clinicId,
        DateTime startInclUtc,
        DateTime endExclUtc,
        Granularity groupBy,
        int? unitId,
        int? attendantId,
        string? source,
        CancellationToken ct)
    {
        // Agregação client-side após trazer (tenantId, createdAt) — simples e sem dependências
        // de funções SQL específicas. Para datasets grandes podemos migrar para date_trunc.
        var q = _db.Leads.AsNoTracking()
            .Where(l => l.TenantId == clinicId
                     && l.CreatedAt >= startInclUtc
                     && l.CreatedAt < endExclUtc);

        if (unitId.HasValue) q = q.Where(l => l.UnitId == unitId.Value);
        if (attendantId.HasValue) q = q.Where(l => l.AttendantId == attendantId.Value);
        if (!string.IsNullOrWhiteSpace(source)) q = q.Where(l => l.Source == source);

        var rows = await q.Select(l => l.CreatedAt).ToListAsync(ct);

        var tz = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");

        var buckets = new SortedDictionary<DateTime, int>();
        foreach (var createdUtc in rows)
        {
            var local = TimeZoneInfo.ConvertTimeFromUtc(
                DateTime.SpecifyKind(createdUtc, DateTimeKind.Utc), tz);
            var key = BucketStart(local, groupBy);
            buckets[key] = buckets.TryGetValue(key, out var c) ? c + 1 : 1;
        }

        // Preenche buckets vazios dentro do intervalo
        var startLocal = TimeZoneInfo.ConvertTimeFromUtc(startInclUtc, tz);
        var endLocal = TimeZoneInfo.ConvertTimeFromUtc(endExclUtc.AddSeconds(-1), tz);
        var cursor = BucketStart(startLocal, groupBy);
        var last = BucketStart(endLocal, groupBy);

        var result = new List<DashboardEvolutionPointDto>();
        while (cursor <= last)
        {
            result.Add(new DashboardEvolutionPointDto
            {
                Bucket = cursor,
                Label = BucketLabel(cursor, groupBy),
                Count = buckets.TryGetValue(cursor, out var c) ? c : 0,
            });
            cursor = NextBucket(cursor, groupBy);
        }
        return result;
    }

    // ════════════════════════════════════════════════════════════════
    //  Origens distintas para popular dropdown de filtro do dashboard
    // ════════════════════════════════════════════════════════════════
    public async Task<List<string>> GetDistinctSourcesAsync(
        int clinicId,
        int? unitId,
        CancellationToken ct = default)
    {
        var q = _db.Leads.AsNoTracking().Where(l => l.TenantId == clinicId);
        if (unitId.HasValue) q = q.Where(l => l.UnitId == unitId.Value);

        return await q
            .Where(l => l.Source != null && l.Source != "")
            .Select(l => l.Source!)
            .Distinct()
            .OrderBy(s => s)
            .ToListAsync(ct);
    }

    // ════════════════════════════════════════════════════════════════
    //  SDRs responsáveis distintos (custom field "Usuário responsável")
    //  para popular o seletor de usuário do dashboard.
    // ════════════════════════════════════════════════════════════════
    public async Task<List<string>> GetDistinctResponsibleUsersAsync(
        int clinicId,
        int? unitId,
        CancellationToken ct = default)
    {
        var q = _db.Leads.AsNoTracking()
            .Where(l => l.TenantId == clinicId && l.CustomFieldsJson != null);
        if (unitId.HasValue) q = q.Where(l => l.UnitId == unitId.Value);

        var jsons = await q.Select(l => l.CustomFieldsJson!).ToListAsync(ct);

        return jsons
            .Select(ResponsibleUserFilter.Extract)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(v => v, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static DateTime BucketStart(DateTime dt, Granularity g) => g switch
    {
        Granularity.Day => new DateTime(dt.Year, dt.Month, dt.Day),
        Granularity.Week => new DateTime(dt.Year, dt.Month, dt.Day).AddDays(-(int)dt.DayOfWeek),
        Granularity.Month => new DateTime(dt.Year, dt.Month, 1),
        Granularity.Quarter => new DateTime(dt.Year, ((dt.Month - 1) / 3) * 3 + 1, 1),
        _ => new DateTime(dt.Year, dt.Month, dt.Day),
    };

    private static DateTime NextBucket(DateTime dt, Granularity g) => g switch
    {
        Granularity.Day => dt.AddDays(1),
        Granularity.Week => dt.AddDays(7),
        Granularity.Month => dt.AddMonths(1),
        Granularity.Quarter => dt.AddMonths(3),
        _ => dt.AddDays(1),
    };

    private static string BucketLabel(DateTime dt, Granularity g) => g switch
    {
        Granularity.Day => dt.ToString("yyyy-MM-dd"),
        Granularity.Week => $"{dt:yyyy-MM-dd}",
        Granularity.Month => dt.ToString("yyyy-MM"),
        Granularity.Quarter => $"{dt.Year}-Q{(dt.Month - 1) / 3 + 1}",
        _ => dt.ToString("yyyy-MM-dd"),
    };
}

public enum ProcessResult
{
    Created,
    Updated,
    Ignored
}