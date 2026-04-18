using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.DTOs.Cloudia;
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

    public async Task<LeadProcessResponseDto> SaveLeadAsync(CloudiaWebhookDto dto)
    {
        return dto.Type switch
        {
            "CUSTOMER_CREATED" => await CreateLeadAsync(dto.Data),
            "CUSTOMER_UPDATED" => await UpdateLeadAsync(dto.Data),
            "CUSTOMER_STAGE_UPDATED" => await UpdateLeadAsync(dto.Data),
            "CUSTOMER_TAGS_UPDATED" => await UpdateUserTagAsync(dto),
            "USER_ASSIGNED_TO_CUSTOMER" => await GetProcessAssignment(dto),
            _ => new LeadProcessResponseDto
            {
                Message = $"Tipo de evento desconhecido: {dto.Type}",
                Result = ProcessResult.Ignored,
            }
        };
    }

    public async Task<List<Lead>> GetAllLeadsAsync()
    {
        return await _db.Leads
            .AsNoTracking()
            .ToListAsync();
    }

    public async Task<LeadDetailDto?> GetLeadByIdAsync(int id)
    {
        var lead = await _db.Leads
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
            .FirstOrDefaultAsync(l => l.Id == id);

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
                .ToList()
        };
    }

    // LeadService.cs - CreateLeadAsync
    private async Task<LeadProcessResponseDto> CreateLeadAsync(CloudiaLeadDataDto dto)
    {
        var externalId = dto.Id;
        var tenantId = dto.ClinicId;

        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation(
            "📥 CreateLeadAsync - DADOS RECEBIDOS:\n" +
            "ExternalId: {ExternalId}\n" +
            "TenantId: {TenantId}\n" +
            "Name: '{Name}'\n" +
            "Phone: '{Phone}'\n" +
            "Email: '{Email}'\n" +
            "Stage: '{Stage}'\n" +
            "IdStage: {IdStage}\n" +
            "Tags: {Tags}\n" +
            "ConversationState: '{ConversationState}'",
            externalId,
            tenantId,
            dto.Name ?? "NULL",
            dto.Phone ?? "NULL",
            dto.Email ?? "NULL",
            dto.Stage ?? "NULL",
            dto.IdStage?.ToString() ?? "NULL",
            dto.Tags is not null ? JsonSerializer.Serialize(dto.Tags) : "NULL",
            dto.ConversationState ?? "NULL"
        );
        }

        // ═══════════════════════════════════════════════════════════════
        // ⚠️ VALIDAÇÃO TEMPORARIAMENTE DESABILITADA
        // ═══════════════════════════════════════════════════════════════
        // TODO: REATIVAR QUANDO O BOT DA CLOUDIA COLETAR TELEFONE SEMPRE
        // ═══════════════════════════════════════════════════════════════
        /*
        // ✅ VERIFICAR SE TEM DADOS MÍNIMOS (PHONE OU EMAIL)
        if (string.IsNullOrWhiteSpace(dto.Phone) && string.IsNullOrWhiteSpace(dto.Email))
        {
            _logger.LogWarning(
                "⚠️ Lead sem telefone e sem email: {ExternalId} / Tenant {TenantId}. Evento ignorado.",
                externalId, tenantId);

            return new LeadProcessResponseDto
            {
                LeadId = externalId,
                Message = "Lead sem telefone/email - aguardando atualização da Cloudia",
                Result = ProcessResult.Ignored,
            };
        }
        */
        // ═══════════════════════════════════════════════════════════════
        // ⚠️ FIM DA VALIDAÇÃO COMENTADA
        // ═══════════════════════════════════════════════════════════════

        var existingLead = await _db.Leads
            .Include(l => l.StageHistory)
            .FirstOrDefaultAsync(l =>
                l.ExternalId == externalId &&
                l.TenantId == tenantId);

        if (existingLead is not null)
        {
            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation("Lead já existe e será ignorado: {ExternalId} / Tenant {TenantId}",
                    externalId, tenantId);
            }

            return new LeadProcessResponseDto
            {
                LeadId = existingLead.Id,
                Message = "Lead já existe, processo de criação ignorado",
                Result = ProcessResult.Ignored,
                Source = existingLead.Source,
                TrackingConfidence = existingLead.TrackingConfidence
            };
        }

        // ✅ ACEITAR PHONE/EMAIL COMO NULL (temporário)
        var phone = dto.Phone ?? "AGUARDANDO_COLETA";
        var email = dto.Email ?? "AGUARDANDO_COLETA";

        var unit = await _unitService.GetOrCreateAsync(dto.ClinicId);
        var stageLabel = dto.Stage;
        var stageId = dto.IdStage;

        // Só tentar buscar OriginEvent se tiver telefone real (não placeholder)
        OriginEvent? originEvent = null;
        if (!string.IsNullOrWhiteSpace(dto.Phone) && dto.Phone != "AGUARDANDO_COLETA")
        {
            originEvent = await _attributionService.FindBestOriginEventAsync(phone, tenantId);
        }

        string source, campaign, confidence;
        string? ad;

        if (originEvent is not null)
        {
            var attribution = _attributionService.ExtractAttributionData(originEvent);
            source = attribution.Source;
            campaign = attribution.Campaign;
            ad = attribution.Ad;
            confidence = attribution.Confidence;

            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation(
                "🎯 INTERCEPTAÇÃO: Lead {Phone} terá origem da Meta: {Source} / {Campaign}",
                phone, source, campaign);
            }
        }
        else
        {
            (source, campaign, ad, confidence) = ResolverTracking(dto);

            if (_logger.IsEnabled(LogLevel.Warning))
            {
                _logger.LogWarning(
                    "⚠️ FALLBACK: Lead {Phone} sem evento da Meta, usando origem Cloudia",
                    phone);
            }
        }

        var channel = ResolverChannel(dto);
        var conversationState = dto.ConversationState ?? "bot";

        var newLead = new Lead
        {
            ExternalId = externalId,
            TenantId = tenantId,

            Name = dto.Name ?? "AGUARDANDO_NOME",
            Phone = phone,
            Email = email,
            Cpf = dto.Cpf,
            Gender = dto.Gender,
            Observations = dto.Observations,

            IdFacebookApp = dto.IdFacebookApp,
            HasHealthInsurancePlan = dto.HasHealthInsurancePlan,
            IdChannelIntegration = dto.IdChannelIntegration,
            ConversationState = dto.ConversationState ?? "bot",

            // 🔥 ATRIBUIÇÃO (da Meta ou Cloudia)
            Source = source,
            Channel = channel,
            Campaign = campaign,
            Ad = ad,
            TrackingConfidence = confidence,

            CurrentStage = stageLabel ?? "SEM_ETAPA",
            CurrentStageId = stageId,

            Status = "new",
            HasAppointment = GetAppointmentAvailable(stageLabel),
            HasPayment = GetHasPayment(stageLabel),

            Tags = dto.Tags is not null && dto.Tags.Count > 0
                ? JsonSerializer.Serialize(dto.Tags)
                : null,

            UnitId = unit.Id,

            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            ConvertedAt = GetHasPayment(stageLabel) ? DateTime.UtcNow : null,

            StageHistory =
            [
                new LeadStageHistory
                {
                    StageId = stageId ?? 0,
                    StageLabel = stageLabel ?? "SEM_ETAPA",
                    ChangedAt = DateTime.UtcNow
                }
            ],
            Conversations = [
                new LeadConversation
                {
                    Channel = channel,
                    Source = source,
                    ConversationState = conversationState,
                    StartedAt = DateTime.UtcNow,

                    Interactions = [
                        new LeadInteraction
                        {
                            Type = "LEAD_CREATED",
                            Content = $"Lead criado via {source}",
                            CreatedAt = DateTime.UtcNow
                        }
                    ]
                }
            ]
        };

        _db.Leads.Add(newLead);
        await _db.SaveChangesAsync();

        if (originEvent is not null)
        {
            await _attributionService.CreateAttributionAsync(
                newLead.Id,
                originEvent,
                phone,
                tenantId);
        }

        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation(
                "✅ Lead criado: {ExternalId} / Tenant {TenantId} / Source: {Source} / Phone: {Phone}",
                externalId, tenantId, source, phone);
        }

        return new LeadProcessResponseDto
        {
            LeadId = newLead.Id,
            Message = "Lead criado",
            Result = ProcessResult.Created,
            Source = newLead.Source,
            TrackingConfidence = newLead.TrackingConfidence
        };
    }

    private async Task<LeadProcessResponseDto> UpdateLeadAsync(CloudiaLeadDataDto dto)
    {
        var externalId = dto.Id;
        var tenantId = dto.ClinicId;

        var lead = await _db.Leads
            .Include(l => l.StageHistory)
            .Include(l => l.Conversations)
                .ThenInclude(c => c.Interactions)
            .Include(l => l.Payments)
            .FirstOrDefaultAsync(l =>
                l.ExternalId == externalId &&
                l.TenantId == tenantId);

        // ✅ SE NÃO EXISTIR, CRIAR AGORA (primeiro UPDATE pode ser o CREATE real)
        if (lead is null)
        {
            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation(
                    "🆕 Lead não encontrado no UPDATE, criando agora: {ExternalId} / Tenant {TenantId}",
                    externalId, tenantId);
            }
            // Chamar CreateLeadAsync
            return await CreateLeadAsync(dto);
        }

        // ────────────────────────────────────────────────────────────────
        // Lead existe, continuar com UPDATE normal
        // ────────────────────────────────────────────────────────────────

        if (_attributionService.ShouldTryImproveAttribution(lead))
        {
            // ✅ Só tentar melhorar se tiver telefone válido
            if (lead.Phone != "AGUARDANDO_COLETA" && !string.IsNullOrWhiteSpace(lead.Phone))
            {
                var normalizedPhone = LeadAttributionService.NormalizePhone(lead.Phone);
                var originEvent = await _attributionService.FindBestOriginEventAsync(normalizedPhone, tenantId);

                if (originEvent is not null && _attributionService.IsEventBetter(originEvent, lead))
                {
                    var attribution = _attributionService.ExtractAttributionData(originEvent);

                    lead.Source = attribution.Source;
                    lead.Campaign = attribution.Campaign;
                    lead.Ad = attribution.Ad;
                    lead.TrackingConfidence = attribution.Confidence;

                    await _attributionService.CreateAttributionAsync(
                        lead.Id,
                        originEvent,
                        normalizedPhone,
                        tenantId);

                    if (_logger.IsEnabled(LogLevel.Information))
                    {
                        _logger.LogInformation(
                        "🔄 MELHORIA: Lead {Phone} teve origem atualizada para {Source}",
                        lead.Phone, lead.Source);
                    }
                }
            }
        }

        if (dto.Name is not null) lead.Name = dto.Name;

        // ✅ Atualizar phone/email se vier com valor real (não null)
        if (dto.Phone is not null)
        {
            lead.Phone = dto.Phone;
            _logger.LogInformation("📞 Telefone atualizado: {Phone} para lead {ExternalId}", dto.Phone, externalId);
        }

        if (dto.Email is not null)
        {
            lead.Email = dto.Email;
            _logger.LogInformation("📧 Email atualizado: {Email} para lead {ExternalId}", dto.Email, externalId);
        }

        if (dto.Cpf is not null) lead.Cpf = dto.Cpf;
        if (dto.Gender is not null) lead.Gender = dto.Gender;
        if (dto.Observations is not null) lead.Observations = dto.Observations;
        if (dto.IdFacebookApp is not null) lead.IdFacebookApp = dto.IdFacebookApp;
        if (dto.HasHealthInsurancePlan.HasValue) lead.HasHealthInsurancePlan = dto.HasHealthInsurancePlan;
        if (dto.IdChannelIntegration.HasValue) lead.IdChannelIntegration = dto.IdChannelIntegration;
        if (dto.LastAdId is not null) lead.LastAdId = dto.LastAdId;
        if (dto.ConversationState is not null) lead.ConversationState = dto.ConversationState;

        if (dto.Tags is not null && dto.Tags.Count > 0)
            lead.Tags = JsonSerializer.Serialize(dto.Tags);

        lead.Channel = ResolverChannel(dto);

        // ── Conversa / ConversationState ──────────────────────────────
        if (dto.ConversationState is not null &&
            dto.ConversationState != lead.ConversationState)
        {
            var conversaAberta = lead.Conversations
                .FirstOrDefault(c => c.EndedAt is null);

            if (conversaAberta is not null)
            {
                conversaAberta.EndedAt = DateTime.UtcNow;
                conversaAberta.Interactions.Add(new LeadInteraction
                {
                    Type = "STATE_CHANGED",
                    Content = $"{lead.ConversationState} → {dto.ConversationState}",
                    CreatedAt = DateTime.UtcNow
                });
            }

            var novaConversa = new LeadConversation
            {
                LeadId = lead.Id,
                Channel = lead.Channel,
                Source = lead.Source,
                ConversationState = dto.ConversationState,
                StartedAt = DateTime.UtcNow,
                Interactions =
                [
                    new LeadInteraction
                    {
                        Type = "STATE_CHANGED",
                        Content = dto.ConversationState,
                        CreatedAt = DateTime.UtcNow
                    }
                ]
            };

            lead.Conversations.Add(novaConversa);
            lead.ConversationState = dto.ConversationState;
        }

        // ── Stage ─────────────────────────────────────────────────────
        if (dto.Stage is not null)
        {
            var novoStage = dto.Stage;
            var novoStageId = dto.IdStage;

            if (lead.CurrentStage != novoStage || lead.CurrentStageId != novoStageId)
            {
                lead.StageHistory.Add(new LeadStageHistory
                {
                    LeadId = lead.Id,
                    StageId = novoStageId ?? 0,
                    StageLabel = novoStage,
                    ChangedAt = DateTime.UtcNow
                });

                var conversaAtiva = lead.Conversations.FirstOrDefault(c => c.EndedAt is null);
                conversaAtiva?.Interactions.Add(new LeadInteraction
                {
                    Type = "STAGE_CHANGED",
                    Content = $"{lead.CurrentStage} → {novoStage}",
                    CreatedAt = DateTime.UtcNow
                });

                lead.CurrentStage = novoStage;
                lead.CurrentStageId = novoStageId;
            }

            var tinhaPayment = lead.HasPayment;
            lead.HasAppointment = GetAppointmentAvailable(novoStage);
            lead.HasPayment = GetHasPayment(novoStage);

            // ── Pagamento ─────────────────────────────────────────────
            if (!tinhaPayment && lead.HasPayment)
            {
                lead.ConvertedAt = DateTime.UtcNow;

                lead.Payments.Add(new Payment
                {
                    LeadId = lead.Id,
                    Amount = 0,
                    PaidAt = DateTime.UtcNow
                });

                var conversaAtiva = lead.Conversations.FirstOrDefault(c => c.EndedAt is null);
                conversaAtiva?.Interactions.Add(new LeadInteraction
                {
                    Type = "PAYMENT",
                    Content = novoStage,
                    CreatedAt = DateTime.UtcNow
                });
            }
        }

        lead.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation("Lead atualizado: {ExternalId} / Tenant {TenantId}", externalId, tenantId);
        }

        return new LeadProcessResponseDto
        {
            LeadId = lead.Id,
            Message = "Lead atualizado",
            Result = ProcessResult.Updated,
            Source = lead.Source,
            TrackingConfidence = lead.TrackingConfidence,
        };
    }

    public async Task<LeadProcessResponseDto> UpdateUserTagAsync(CloudiaWebhookDto dto)
    {
        var externalId = dto?.Data?.Id;
        var tenantId = dto?.Data?.ClinicId;

        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation("Tags recebidas para {ExternalId}: {Tags}",
                externalId,
                JsonSerializer.Serialize(dto?.Data?.Tags));
        }

        var lead = await _db.Leads
            .FirstOrDefaultAsync(l =>
                l.ExternalId == externalId &&
                l.TenantId == tenantId);

        if (lead is null)
        {
            _logger.LogWarning("Lead não encontrado para atualizar tags: {ExternalId} / Tenant {TenantId}",
                externalId, tenantId);
            return new LeadProcessResponseDto
            {
                LeadId = null,
                Message = "Lead não encontrado para atualização de tags",
                Result = ProcessResult.Ignored,
                Source = null,
                TrackingConfidence = null
            };
        }

        if (dto?.Data?.Tags is not null && dto.Data.Tags.Count > 0)
            lead.Tags = JsonSerializer.Serialize(dto.Data.Tags);

        lead.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation("Tags do lead atualizadas: {ExternalId} / Tenant {TenantId}", externalId, tenantId);
        }
        return new LeadProcessResponseDto
        {
            LeadId = lead.Id,
            Message = "Tags atualizadas",
            Result = ProcessResult.Updated,
            Source = lead.Source,
            TrackingConfidence = lead.TrackingConfidence
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
    public async Task<int> GetLeadsInServiceCountAsync(int? unitId = null)
    {
        var query = _db.Leads
            .AsNoTracking()
            .Where(l => l.ConversationState == "service");

        // Filtrar por unidade se especificado
        if (unitId.HasValue)
        {
            query = query.Where(l => l.UnitId == unitId.Value);
        }

        var count = await query.CountAsync();

        _logger.LogInformation(
            "📊 Leads em atendimento: {Count} (unitId: {UnitId})", 
            count, unitId);

        return count;
    }

    /// <summary>
    /// Contar leads em cada estado
    /// </summary>
    public async Task<LeadsInServiceDto> GetLeadsInServiceDetailsAsync(int? unitId = null)
    {
        var query = _db.Leads.AsNoTracking();

        if (unitId.HasValue)
        {
            query = query.Where(l => l.UnitId == unitId.Value);
        }

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

    public async Task<int> GetCheckClosedQueries(int clinicId)
    {
        return await _db.Leads
            .AsNoTracking()
            .Where(l =>
                l.UnitId == clinicId &&
                (l.CurrentStage == "10_EM_TRATAMENTO" || l.CurrentStage == "09_FECHOU_TRATAMENTO"))
            .Select(l => l.Id)
            .Distinct()
            .CountAsync();
    }

    public async Task<int> GetCheckStageWithoutPayment(int clinicId)
    {
        return await _db.Leads
            .AsNoTracking()
            .Where(l =>
                l.UnitId == clinicId &&
                l.CurrentStage == "04_AGENDADO_SEM_PAGAMENTO")
            .CountAsync();
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

    public async Task<List<OrigemAgrupadaDto>> GetCheckSourceCloudia(int clinicId)
    {
        return await _db.Leads
            .AsNoTracking()
            .Where(l => l.TenantId == clinicId)
            .GroupBy(l => l.Source)
            .Select(g => new OrigemAgrupadaDto
            {
                Origem = g.Key,
                Quantidade = g.Count()
            })
            .ToListAsync();
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

    private static (string Source, string Campaign, string? Ad, string Confidence) ResolverTracking(CloudiaLeadDataDto dto)
    {
        if (dto.AdData is not null && dto.AdData.Count > 0)
        {
            var item = dto.AdData.First();

            var source = !string.IsNullOrWhiteSpace(item.Source)
                ? item.Source.Trim().ToUpperInvariant()
                : (!string.IsNullOrWhiteSpace(dto.Origin)
                    ? dto.Origin.Trim().ToUpperInvariant()
                    : "DESCONHECIDO");

            var campaign = !string.IsNullOrWhiteSpace(item.AdId)
                ? item.AdId.Trim()
                : "DESCONHECIDO";

            var ad = !string.IsNullOrWhiteSpace(item.AdName)
                ? item.AdName.Trim()
                : "DESCONHECIDO";

            return (source, campaign, ad, "ALTA");
        }

        if (!string.IsNullOrWhiteSpace(dto.Origin))
        {
            return (dto.Origin.Trim().ToUpperInvariant(), "DESCONHECIDO", "DESCONHECIDO", "MEDIA");
        }

        return ("DESCONHECIDO", "DESCONHECIDO", "DESCONHECIDO", "BAIXA");
    }

    private static string ResolverChannel(CloudiaLeadDataDto dto)
    {
        if (dto.RegisteredOnWhatsApp == 1 ||
            !string.IsNullOrWhiteSpace(dto.IdWhatsApp) ||
            dto.IdChannelIntegration.HasValue)
        {
            return "WHATSAPP";
        }

        return "DESCONHECIDO";
    }

    private static bool GetAppointmentAvailable(string? stage)
    {
        if (string.IsNullOrWhiteSpace(stage))
            return false;

        return stage is "04_AGENDADO_SEM_PAGAMENTO"
            or "05_AGENDADO_COM_PAGAMENTO"
            or "09_FECHOU_TRATAMENTO"
            or "10_EM_TRATAMENTO";
    }

    private static bool GetHasPayment(string? stage)
    {
        if (string.IsNullOrWhiteSpace(stage))
            return false;

        return stage is "05_AGENDADO_COM_PAGAMENTO"
            or "09_FECHOU_TRATAMENTO"
            or "10_EM_TRATAMENTO";
    }

    private async Task<LeadProcessResponseDto> GetProcessAssignment(CloudiaWebhookDto dto)
    {
        var externalUserId = dto.AssignedUserId!.Value;
        var externalLeadId = dto.Customer!.Id;
        var tenantId = dto.Customer.ClinicId;
        var conversationState = dto.Data?.ConversationState ?? "service";

        var lead = await _db.Leads
            .Include(l => l.Unit)  // ✅ CRÍTICO: Incluir Unit
            .FirstOrDefaultAsync(l =>
            l.ExternalId == externalLeadId &&
            l.TenantId == tenantId);


        if (lead is null)
        {
            _logger.LogWarning("Lead não encontrado para atribuição: {LeadId}", externalLeadId);
            return new LeadProcessResponseDto
            {
                Result = ProcessResult.Ignored,
                LeadId = null,
                Message = "Lead não encontrado"
            };
        }

        if (!lead.UnitId.HasValue)
        {
            _logger.LogError(
                "Lead {LeadId} não tem UnitId! Criando unit agora...", 
                externalLeadId);
            
            var unit = await _unitService.GetOrCreateAsync(tenantId);
            lead.UnitId = unit.Id;
            await _db.SaveChangesAsync();
        }
        var attendant = await _attendantService.GetOrCreateAsync(
            externalUserId,
            dto.AssignedUserName!,
            dto.AssignedUserEmail,
            lead.UnitId!.Value);

        lead.AttendantId = attendant.Id;
        lead.UpdatedAt = DateTime.UtcNow;
        lead.ConversationState = conversationState;

        _db.LeadAssignments.Add(new LeadAssignment
        {
            LeadId = lead.Id,
            AttendantId = attendant.Id,
            Stage = dto.Customer.Stage,
            AssignedAt = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();

        _logger.LogInformation("Lead {LeadId} atribuído para {Name}", externalLeadId, dto.AssignedUserName);
        return new LeadProcessResponseDto
        {
            LeadId = lead.Id,
            Message = $"Lead atribuído para {dto.AssignedUserName}",
            Result = ProcessResult.Updated,
            Source = lead.Source,
            TrackingConfidence = lead.TrackingConfidence,
        };
    }
            // ═══════════════════════════════════════════════════════════════
    // ADICIONAR NO LeadService.cs
    // ═══════════════════════════════════════════════════════════════

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
    //  Dashboard overview — tudo que o dashboard precisa num request,
    //  filtrado por dateFrom/dateTo em Lead.CreatedAt.
    // ════════════════════════════════════════════════════════════════

    public async Task<DashboardOverviewDto> GetDashboardOverviewAsync(
        int clinicId,
        DateTime dateFrom,
        DateTime dateTo,
        int? unitId,
        CancellationToken ct = default)
    {
        if (dateTo < dateFrom) throw new ArgumentException("dateTo deve ser >= dateFrom");

        // Janela UTC com fim exclusivo (+1 dia para incluir o dia inteiro)
        var startUtc = DateTime.SpecifyKind(dateFrom.Date, DateTimeKind.Utc);
        var endExclUtc = DateTime.SpecifyKind(dateTo.Date.AddDays(1), DateTimeKind.Utc);

        var baseQ = _db.Leads.AsNoTracking()
            .Where(l => l.TenantId == clinicId
                     && l.CreatedAt >= startUtc
                     && l.CreatedAt < endExclUtc);

        if (unitId.HasValue) baseQ = baseQ.Where(l => l.UnitId == unitId.Value);

        var totalLeads = await baseQ.CountAsync(ct);

        // KPIs por etapa
        var consultas = await baseQ
            .Where(l => l.CurrentStage == "10_EM_TRATAMENTO" || l.CurrentStage == "09_FECHOU_TRATAMENTO")
            .CountAsync(ct);
        var comPag = await baseQ
            .Where(l => l.CurrentStage == "05_AGENDADO_COM_PAGAMENTO")
            .CountAsync(ct);
        var semPag = await baseQ
            .Where(l => l.CurrentStage == "04_AGENDADO_SEM_PAGAMENTO")
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

        var conversaoRate = totalLeads > 0 ? consultas * 100.0 / totalLeads : 0;
        var pagamentoRate = totalLeads > 0 ? comPag * 100.0 / totalLeads : 0;
        var semPagRate = totalLeads > 0 ? semPag * 100.0 / totalLeads : 0;

        return new DashboardOverviewDto
        {
            DateFrom = dateFrom.Date,
            DateTo = dateTo.Date,
            TotalLeads = totalLeads,
            Consultas = consultas,
            ComPagamento = comPag,
            SemPagamento = semPag,
            ConversaoRate = Math.Round(conversaoRate, 2),
            PagamentoRate = Math.Round(pagamentoRate, 2),
            SemPagamentoRate = Math.Round(semPagRate, 2),
            States = states,
            Etapas = etapas,
            Origens = origens,
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
        CancellationToken ct = default)
    {
        if (dateTo < dateFrom) throw new ArgumentException("dateTo deve ser >= dateFrom");

        // Normaliza pro início/fim do dia em UTC (+1 dia no fim inclusivo)
        var startUtc = DateTime.SpecifyKind(dateFrom.Date, DateTimeKind.Utc);
        var endExclUtc = DateTime.SpecifyKind(dateTo.Date.AddDays(1), DateTimeKind.Utc);

        var currentPoints = await BucketSeriesAsync(clinicId, startUtc, endExclUtc, groupBy, ct);

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

            comparePoints = await BucketSeriesAsync(clinicId, cStart, cEnd, groupBy, ct);
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
        CancellationToken ct)
    {
        // Agregação client-side após trazer (tenantId, createdAt) — simples e sem dependências
        // de funções SQL específicas. Para datasets grandes podemos migrar para date_trunc.
        var rows = await _db.Leads.AsNoTracking()
            .Where(l => l.TenantId == clinicId
                     && l.CreatedAt >= startInclUtc
                     && l.CreatedAt < endExclUtc)
            .Select(l => l.CreatedAt)
            .ToListAsync(ct);

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