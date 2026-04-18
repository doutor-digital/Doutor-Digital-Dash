using System.Text.Json;
using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.DTOs.Filter;
using LeadAnalytics.Api.DTOs.Response;
using LeadAnalytics.Api.Models;
using LeadAnalytics.Api.Service.Filtering;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace LeadAnalytics.Api.Service;

public class ContactService(AppDbContext db, IMemoryCache? cache = null)
{
    private readonly AppDbContext _db = db;
    private readonly IMemoryCache? _cache = cache;
    private static readonly TimeSpan FilterOptionsTtl = TimeSpan.FromMinutes(5);

    public static readonly string[] AllowedAttendance = new[] { "compareceu", "faltou", "aguardando" };

    public async Task<ContactsListResponseDto> ListAsync(
        int tenantId,
        ContactFiltersDto filters,
        int page = 1,
        int pageSize = 50,
        string orderBy = "created_at",
        string orderDir = "desc",
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var origem = string.IsNullOrWhiteSpace(filters.Origem) ? "all" : filters.Origem;
        var search = filters.Search;
        var normalized = string.IsNullOrWhiteSpace(search) ? null : NormalizeForSearch(search);
        var statusFilter = NormalizeStatus(filters.Status);

        // ── COUNTS (por origem e por ação — respeitam tenant, ignoram demais filtros) ──
        var importTotal = await _db.Contacts
            .Where(c => c.TenantId == tenantId && c.Origem == "import_csv")
            .CountAsync(ct);
        var manualTotal = await _db.Contacts
            .Where(c => c.TenantId == tenantId && c.Origem == "manual")
            .CountAsync(ct);
        var webhookTotal = await _db.Leads
            .Where(l => l.TenantId == tenantId)
            .CountAsync(ct);

        var compareceuContacts = await _db.Contacts
            .Where(c => c.TenantId == tenantId && c.AttendanceStatus == "compareceu").CountAsync(ct);
        var faltouContacts = await _db.Contacts
            .Where(c => c.TenantId == tenantId && c.AttendanceStatus == "faltou").CountAsync(ct);
        var aguardandoContacts = await _db.Contacts
            .Where(c => c.TenantId == tenantId && c.AttendanceStatus == "aguardando").CountAsync(ct);

        var compareceuLeads = await _db.Leads
            .Where(l => l.TenantId == tenantId && l.AttendanceStatus == "compareceu").CountAsync(ct);
        var faltouLeads = await _db.Leads
            .Where(l => l.TenantId == tenantId && l.AttendanceStatus == "faltou").CountAsync(ct);
        var aguardandoLeads = await _db.Leads
            .Where(l => l.TenantId == tenantId && l.AttendanceStatus == "aguardando").CountAsync(ct);

        var counts = new ContactCountsDto
        {
            ImportCsv = importTotal,
            Manual = manualTotal,
            WebhookCloudia = webhookTotal,
            All = importTotal + manualTotal + webhookTotal,
            Compareceu = compareceuContacts + compareceuLeads,
            Faltou = faltouContacts + faltouLeads,
            Aguardando = aguardandoContacts + aguardandoLeads,
        };

        // ── DATA ──
        var items = new List<ContactDto>();
        var total = 0;

        var includeContacts = origem == "import_csv" || origem == "manual" || origem == "all";
        var includeLeads = origem == "webhook_cloudia" || origem == "all";

        IQueryable<Contact>? contactsQ = null;
        IQueryable<Lead>? leadsQ = null;

        if (includeContacts)
        {
            contactsQ = _db.Contacts.AsNoTracking().Where(c => c.TenantId == tenantId);
            if (origem == "import_csv") contactsQ = contactsQ.Where(c => c.Origem == "import_csv");
            else if (origem == "manual") contactsQ = contactsQ.Where(c => c.Origem == "manual");

            contactsQ = ApplyContactFilters(contactsQ, filters, statusFilter, normalized);
        }

        if (includeLeads)
        {
            leadsQ = _db.Leads.AsNoTracking().Where(l => l.TenantId == tenantId);
            leadsQ = ApplyLeadFilters(leadsQ, filters, statusFilter, normalized);
        }

        if (origem == "import_csv" || origem == "manual")
        {
            var q = contactsQ!;
            total = await q.CountAsync(ct);
            var rows = await ApplyOrder(q, orderBy, orderDir)
                .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
            items.AddRange(rows.Select(ToDto));
        }
        else if (origem == "webhook_cloudia")
        {
            var q = leadsQ!;
            total = await q.CountAsync(ct);
            var rows = await ApplyLeadOrder(q, orderBy, orderDir)
                .Include(l => l.Unit)
                .Include(l => l.Attendant)
                .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
            items.AddRange(rows.Select(ToDtoFromLead));
        }
        else // all → merge em memória com teto
        {
            var take = page * pageSize + pageSize;

            var contactList = await contactsQ!
                .OrderByDescending(c => c.LastMessageAt ?? c.CreatedAt)
                .Take(take).ToListAsync(ct);
            var leadList = await leadsQ!
                .OrderByDescending(l => l.UpdatedAt)
                .Take(take).ToListAsync(ct);

            var merged = contactList.Select(ToDto)
                .Concat(leadList.Select(ToDtoFromLead))
                .OrderByDescending(c => c.LastMessageAt ?? DateTime.MinValue)
                .ToList();

            total = await contactsQ!.CountAsync(ct) + await leadsQ!.CountAsync(ct);

            items = merged.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        }

        var totalPages = (int)Math.Ceiling(total / (double)pageSize);

        return new ContactsListResponseDto
        {
            Data = items,
            Pagination = new ContactPaginationDto
            {
                Page = page,
                PageSize = pageSize,
                Total = total,
                TotalPages = totalPages <= 0 ? 1 : totalPages,
            },
            Counts = counts,
        };
    }

    private static IQueryable<Contact> ApplyContactFilters(
        IQueryable<Contact> q,
        ContactFiltersDto f,
        string? statusFilter,
        string? normalizedPhone)
    {
        if (!string.IsNullOrWhiteSpace(f.Search))
        {
            var s = f.Search.ToLower();
            q = q.Where(c =>
                c.Name.ToLower().Contains(s) ||
                c.PhoneNormalized.Contains(normalizedPhone ?? s) ||
                (c.PhoneRaw != null && c.PhoneRaw.Contains(s)));
        }

        if (statusFilter is not null)
        {
            if (statusFilter == "none")
                q = q.Where(c => c.AttendanceStatus == null);
            else
                q = q.Where(c => c.AttendanceStatus == statusFilter);
        }

        if (!string.IsNullOrWhiteSpace(f.Etapa))
        {
            var et = f.Etapa;
            q = q.Where(c => c.Etapa != null && c.Etapa.ToLower() == et.ToLower());
        }

        if (!string.IsNullOrWhiteSpace(f.Tag))
        {
            var tag = f.Tag;
            q = q.Where(c => c.TagsJson != null && c.TagsJson.ToLower().Contains(tag.ToLower()));
        }

        if (f.Blocked.HasValue)
            q = q.Where(c => c.Blocked == f.Blocked.Value);

        if (f.HasConsultation.HasValue)
        {
            q = f.HasConsultation.Value
                ? q.Where(c => c.ConsultationAt != null)
                : q.Where(c => c.ConsultationAt == null);
        }

        if (f.DateFrom.HasValue) q = q.Where(c => c.CreatedAt >= f.DateFrom.Value);
        if (f.DateTo.HasValue) q = q.Where(c => c.CreatedAt <= f.DateTo.Value);

        return q;
    }

    private static IQueryable<Lead> ApplyLeadFilters(
        IQueryable<Lead> q,
        ContactFiltersDto f,
        string? statusFilter,
        string? normalizedPhone)
    {
        if (!string.IsNullOrWhiteSpace(f.Search))
        {
            var s = f.Search.ToLower();
            q = q.Where(l =>
                l.Name.ToLower().Contains(s) ||
                l.Phone.Contains(normalizedPhone ?? s));
        }

        if (statusFilter is not null)
        {
            if (statusFilter == "none")
                q = q.Where(l => l.AttendanceStatus == null);
            else
                q = q.Where(l => l.AttendanceStatus == statusFilter);
        }

        if (!string.IsNullOrWhiteSpace(f.Etapa))
        {
            var et = f.Etapa;
            q = q.Where(l => l.CurrentStage != null && l.CurrentStage.ToLower() == et.ToLower());
        }

        if (!string.IsNullOrWhiteSpace(f.Tag))
        {
            var tag = f.Tag;
            q = q.Where(l => l.Tags != null && l.Tags.ToLower().Contains(tag.ToLower()));
        }

        if (f.Blocked.HasValue && f.Blocked.Value)
            q = q.Where(_ => false); // leads não têm flag blocked — retornar vazio se pedirem blocked=true

        if (f.HasConsultation.HasValue)
            q = q.Where(l => l.HasAppointment == f.HasConsultation.Value);

        if (f.DateFrom.HasValue) q = q.Where(l => l.CreatedAt >= f.DateFrom.Value);
        if (f.DateTo.HasValue) q = q.Where(l => l.CreatedAt <= f.DateTo.Value);

        return q;
    }

    public async Task<ContactDetailDto?> GetByIdAsync(
        int tenantId,
        string id,
        CancellationToken ct = default)
    {
        var (prefix, numId) = ParseId(id);
        if (numId is null) return null;

        if (prefix == "c" || prefix == "auto")
        {
            var contact = await _db.Contacts
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == numId && c.TenantId == tenantId, ct);
            if (contact is not null) return ToDetail(contact);
            if (prefix == "c") return null;
        }

        var lead = await _db.Leads
            .AsNoTracking()
            .Include(l => l.Unit)
            .Include(l => l.Attendant)
            .FirstOrDefaultAsync(l => l.Id == numId && l.TenantId == tenantId, ct);

        return lead is null ? null : ToDetail(lead);
    }

    public async Task<ContactDetailDto> CreateAsync(
        ContactCreateDto dto,
        CancellationToken ct = default)
    {
        var name = dto.Name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("nome é obrigatório", nameof(dto.Name));

        var phoneNorm = ContactImportService.NormalizePhone(dto.Phone ?? string.Empty)
            ?? throw new ArgumentException("telefone inválido", nameof(dto.Phone));

        var existing = await _db.Contacts.FirstOrDefaultAsync(
            c => c.TenantId == dto.ClinicId && c.PhoneNormalized == phoneNorm, ct);
        if (existing is not null)
            throw new InvalidOperationException($"contato já existe (id c_{existing.Id})");

        var attendance = NormalizeStatus(dto.AttendanceStatus);
        if (attendance == "none") attendance = null;

        var tagsJson = (dto.Tags is { Count: > 0 })
            ? JsonSerializer.Serialize(dto.Tags.Select(t => t.Trim()).Where(t => t.Length > 0).ToList())
            : null;

        var now = DateTime.UtcNow;
        var contact = new Contact
        {
            TenantId = dto.ClinicId,
            Name = name,
            PhoneNormalized = phoneNorm,
            PhoneRaw = dto.Phone,
            Origem = "manual",
            Conexao = string.IsNullOrWhiteSpace(dto.Conexao) ? null : dto.Conexao.Trim(),
            Observacoes = string.IsNullOrWhiteSpace(dto.Observacoes) ? null : dto.Observacoes.Trim(),
            Etapa = string.IsNullOrWhiteSpace(dto.Etapa) ? null : dto.Etapa.Trim(),
            TagsJson = tagsJson,
            ConsultationAt = dto.ConsultationAt,
            ConsultationRegisteredAt = dto.ConsultationAt.HasValue ? now : null,
            Birthday = dto.Birthday,
            AttendanceStatus = attendance,
            AttendanceStatusAt = attendance is null ? null : now,
            CreatedAt = now,
            UpdatedAt = now,
        };

        _db.Contacts.Add(contact);
        await _db.SaveChangesAsync(ct);

        return ToDetail(contact);
    }

    public async Task<ContactDetailDto?> UpdateAsync(
        int tenantId,
        string id,
        ContactUpdateDto dto,
        CancellationToken ct = default)
    {
        var (prefix, numId) = ParseId(id);
        if (numId is null) return null;
        if (prefix == "l")
            throw new InvalidOperationException("leads não podem ser editados por este endpoint");

        var contact = await _db.Contacts
            .FirstOrDefaultAsync(c => c.Id == numId && c.TenantId == tenantId, ct);
        if (contact is null) return null;

        var now = DateTime.UtcNow;

        if (!string.IsNullOrWhiteSpace(dto.Name))
            contact.Name = dto.Name.Trim();

        if (!string.IsNullOrWhiteSpace(dto.Phone))
        {
            var phoneNorm = ContactImportService.NormalizePhone(dto.Phone)
                ?? throw new ArgumentException("telefone inválido", nameof(dto.Phone));
            if (phoneNorm != contact.PhoneNormalized)
            {
                var conflict = await _db.Contacts.AnyAsync(
                    c => c.TenantId == tenantId &&
                         c.Id != contact.Id &&
                         c.PhoneNormalized == phoneNorm, ct);
                if (conflict)
                    throw new InvalidOperationException("outro contato já usa esse telefone");
                contact.PhoneNormalized = phoneNorm;
                contact.PhoneRaw = dto.Phone;
            }
        }

        if (dto.Conexao is not null)
            contact.Conexao = string.IsNullOrWhiteSpace(dto.Conexao) ? null : dto.Conexao.Trim();
        if (dto.Observacoes is not null)
            contact.Observacoes = string.IsNullOrWhiteSpace(dto.Observacoes) ? null : dto.Observacoes.Trim();
        if (dto.Etapa is not null)
            contact.Etapa = string.IsNullOrWhiteSpace(dto.Etapa) ? null : dto.Etapa.Trim();
        if (dto.Tags is not null)
        {
            var clean = dto.Tags.Select(t => t.Trim()).Where(t => t.Length > 0).ToList();
            contact.TagsJson = clean.Count > 0 ? JsonSerializer.Serialize(clean) : null;
        }
        if (dto.ConsultationAt.HasValue)
        {
            contact.ConsultationAt = dto.ConsultationAt;
            contact.ConsultationRegisteredAt = now;
        }
        if (dto.Birthday.HasValue) contact.Birthday = dto.Birthday;
        if (dto.Blocked.HasValue) contact.Blocked = dto.Blocked.Value;

        if (dto.AttendanceStatus is not null)
        {
            var attendance = NormalizeStatus(dto.AttendanceStatus);
            if (attendance == "none") attendance = null;
            contact.AttendanceStatus = attendance;
            contact.AttendanceStatusAt = attendance is null ? null : now;
        }

        contact.UpdatedAt = now;
        await _db.SaveChangesAsync(ct);
        return ToDetail(contact);
    }

    public async Task<bool> DeleteAsync(
        int tenantId,
        string id,
        CancellationToken ct = default)
    {
        var (prefix, numId) = ParseId(id);
        if (numId is null) return false;
        if (prefix == "l")
            throw new InvalidOperationException("leads não podem ser removidos por este endpoint");

        var contact = await _db.Contacts
            .FirstOrDefaultAsync(c => c.Id == numId && c.TenantId == tenantId, ct);
        if (contact is null) return false;

        _db.Contacts.Remove(contact);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<ContactDetailDto?> SetActionAsync(
        int tenantId,
        string id,
        string action,
        DateTime? consultationAt,
        string? observacoes,
        CancellationToken ct = default)
    {
        var normalized = NormalizeStatus(action)
            ?? throw new ArgumentException("ação inválida (use compareceu, faltou ou aguardando)", nameof(action));
        if (normalized == "none") normalized = null;

        var (prefix, numId) = ParseId(id);
        if (numId is null) return null;

        var now = DateTime.UtcNow;

        if (prefix == "c" || prefix == "auto")
        {
            var contact = await _db.Contacts
                .FirstOrDefaultAsync(c => c.Id == numId && c.TenantId == tenantId, ct);
            if (contact is not null)
            {
                contact.AttendanceStatus = normalized;
                contact.AttendanceStatusAt = normalized is null ? null : now;
                if (consultationAt.HasValue)
                {
                    contact.ConsultationAt = consultationAt.Value;
                    contact.ConsultationRegisteredAt = now;
                }
                if (!string.IsNullOrWhiteSpace(observacoes)) contact.Observacoes = observacoes.Trim();
                contact.UpdatedAt = now;
                await _db.SaveChangesAsync(ct);
                return ToDetail(contact);
            }
            if (prefix == "c") return null;
        }

        var lead = await _db.Leads
            .Include(l => l.Unit).Include(l => l.Attendant)
            .FirstOrDefaultAsync(l => l.Id == numId && l.TenantId == tenantId, ct);
        if (lead is null) return null;

        lead.AttendanceStatus = normalized;
        lead.AttendanceStatusAt = normalized is null ? null : now;
        if (normalized == "compareceu") lead.HasAppointment = true;
        if (!string.IsNullOrWhiteSpace(observacoes)) lead.Observations = observacoes.Trim();
        lead.UpdatedAt = now;
        await _db.SaveChangesAsync(ct);
        return ToDetail(lead);
    }

    private static (string prefix, int? id) ParseId(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return ("", null);

        string prefix; string numeric;
        if (id.StartsWith("c_", StringComparison.OrdinalIgnoreCase)) { prefix = "c"; numeric = id[2..]; }
        else if (id.StartsWith("l_", StringComparison.OrdinalIgnoreCase)) { prefix = "l"; numeric = id[2..]; }
        else { prefix = "auto"; numeric = id; }

        return int.TryParse(numeric, out var n) ? (prefix, n) : (prefix, null);
    }

    private static string? NormalizeStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status)) return null;
        var s = status.Trim().ToLowerInvariant();
        if (s == "all") return null;
        if (s == "none" || s == "sem" || s == "sem_acao" || s == "null") return "none";
        return AllowedAttendance.Contains(s) ? s : null;
    }

    private static ContactDetailDto ToDetail(Models.Contact c)
    {
        return new ContactDetailDto
        {
            Id = $"c_{c.Id}",
            TenantId = c.TenantId,
            Source = "contact",
            Name = c.Name,
            PhoneNormalized = c.PhoneNormalized,
            PhoneRaw = c.PhoneRaw,
            Origem = c.Origem,
            Conexao = c.Conexao,
            Observacoes = c.Observacoes,
            Etapa = c.Etapa,
            Tags = DeserializeStringList(c.TagsJson),
            MetaAdsIds = DeserializeStringList(c.MetaAdsIdsJson),
            ConsultationAt = c.ConsultationAt,
            ConsultationRegisteredAt = c.ConsultationRegisteredAt,
            LastMessageAt = c.LastMessageAt,
            Birthday = c.Birthday,
            Blocked = c.Blocked,
            AttendanceStatus = c.AttendanceStatus,
            AttendanceStatusAt = c.AttendanceStatusAt,
            ImportedAt = c.ImportedAt,
            ImportBatchId = c.ImportBatchId,
            CreatedAt = c.CreatedAt,
            UpdatedAt = c.UpdatedAt,
        };
    }

    private static ContactDetailDto ToDetail(Models.Lead l)
    {
        var phone = l.Phone == "AGUARDANDO_COLETA" ? "" : l.Phone;
        var email = l.Email == "AGUARDANDO_COLETA" ? null : l.Email;

        return new ContactDetailDto
        {
            Id = $"l_{l.Id}",
            TenantId = l.TenantId,
            Source = "lead",
            Name = l.Name,
            PhoneNormalized = OnlyDigits(phone),
            PhoneRaw = phone,
            Origem = "webhook_cloudia",
            Etapa = l.CurrentStage,
            Tags = DeserializeStringList(l.Tags),
            Blocked = false,
            AttendanceStatus = l.AttendanceStatus,
            AttendanceStatusAt = l.AttendanceStatusAt,

            ExternalId = l.ExternalId,
            Email = email,
            Cpf = l.Cpf,
            Gender = l.Gender,
            Channel = l.Channel,
            Campaign = l.Campaign,
            Ad = l.Ad,
            TrackingConfidence = l.TrackingConfidence,
            CurrentStage = l.CurrentStage,
            HasAppointment = l.HasAppointment,
            HasPayment = l.HasPayment,
            HasHealthInsurancePlan = l.HasHealthInsurancePlan,
            ConversationState = l.ConversationState,
            UnitId = l.UnitId,
            UnitName = l.Unit?.Name,
            AttendantId = l.AttendantId,
            AttendantName = l.Attendant?.Name,
            AttendantEmail = l.Attendant?.Email,
            ConvertedAt = l.ConvertedAt,

            CreatedAt = l.CreatedAt,
            UpdatedAt = l.UpdatedAt,
        };
    }

    private static List<string> DeserializeStringList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? new();
        }
        catch
        {
            return new() { json };
        }
    }

    private static IQueryable<Models.Contact> ApplyOrder(
        IQueryable<Models.Contact> q, string orderBy, string orderDir)
    {
        var desc = orderDir?.ToLower() == "desc";
        return orderBy?.ToLower() switch
        {
            "name" => desc ? q.OrderByDescending(c => c.Name) : q.OrderBy(c => c.Name),
            "last_message_at" => desc
                ? q.OrderByDescending(c => c.LastMessageAt)
                : q.OrderBy(c => c.LastMessageAt),
            "attendance_status_at" => desc
                ? q.OrderByDescending(c => c.AttendanceStatusAt)
                : q.OrderBy(c => c.AttendanceStatusAt),
            _ => desc ? q.OrderByDescending(c => c.CreatedAt) : q.OrderBy(c => c.CreatedAt),
        };
    }

    private static IQueryable<Models.Lead> ApplyLeadOrder(
        IQueryable<Models.Lead> q, string orderBy, string orderDir)
    {
        var desc = orderDir?.ToLower() == "desc";
        return orderBy?.ToLower() switch
        {
            "name" => desc ? q.OrderByDescending(l => l.Name) : q.OrderBy(l => l.Name),
            "attendance_status_at" => desc
                ? q.OrderByDescending(l => l.AttendanceStatusAt)
                : q.OrderBy(l => l.AttendanceStatusAt),
            _ => desc ? q.OrderByDescending(l => l.UpdatedAt) : q.OrderBy(l => l.UpdatedAt),
        };
    }

    private static ContactDto ToDto(Models.Contact c)
    {
        var tags = new List<string>();
        if (!string.IsNullOrWhiteSpace(c.TagsJson))
        {
            try { tags = JsonSerializer.Deserialize<List<string>>(c.TagsJson) ?? new(); }
            catch { tags = new() { c.TagsJson }; }
        }

        return new ContactDto
        {
            Id = $"c_{c.Id}",
            Name = c.Name,
            PhoneNormalized = c.PhoneNormalized,
            Origem = c.Origem,
            Etapa = c.Etapa,
            Tags = tags,
            LastMessageAt = c.LastMessageAt,
            Blocked = c.Blocked,
            AttendanceStatus = c.AttendanceStatus,
            AttendanceStatusAt = c.AttendanceStatusAt,
            ImportedAt = c.ImportedAt,
        };
    }

    private static ContactDto ToDtoFromLead(Models.Lead l)
    {
        var tags = new List<string>();
        if (!string.IsNullOrWhiteSpace(l.Tags))
        {
            try { tags = JsonSerializer.Deserialize<List<string>>(l.Tags) ?? new(); }
            catch { tags = new() { l.Tags }; }
        }

        var phone = l.Phone == "AGUARDANDO_COLETA" ? "" : l.Phone;

        return new ContactDto
        {
            Id = $"l_{l.Id}",
            Name = l.Name,
            PhoneNormalized = OnlyDigits(phone),
            Origem = "webhook_cloudia",
            Etapa = l.CurrentStage,
            Tags = tags,
            LastMessageAt = l.UpdatedAt,
            Blocked = false,
            AttendanceStatus = l.AttendanceStatus,
            AttendanceStatusAt = l.AttendanceStatusAt,
            ImportedAt = null,
        };
    }

    private static string OnlyDigits(string s)
    {
        var sb = new System.Text.StringBuilder(s.Length);
        foreach (var c in s) if (char.IsDigit(c)) sb.Append(c);
        return sb.ToString();
    }

    private static string NormalizeForSearch(string s)
    {
        return OnlyDigits(s);
    }

    // ════════════════════════════════════════════════════════════════
    //  Busca avançada com DSL (POST /contacts/search)
    // ════════════════════════════════════════════════════════════════

    public async Task<ContactSearchResponseDto> SearchAsync(
        int tenantId,
        ContactSearchRequestDto req,
        CancellationToken ct = default)
    {
        var page = Math.Max(1, req.Page);
        var pageSize = Math.Clamp(req.PageSize, 1, 200);
        var origem = string.IsNullOrWhiteSpace(req.Origem) ? "all" : req.Origem.ToLowerInvariant();
        var search = req.Search;
        var filters = req.Filters ?? new List<FilterCriterionDto>();

        // Base parametrizada por tenant. Qualquer consulta SEMPRE começa por tenant.
        var baseQuery = _db.Contacts.AsNoTracking().Where(c => c.TenantId == tenantId);

        // Busca livre (nome + telefone normalizado)
        if (!string.IsNullOrWhiteSpace(search))
        {
            var sLower = search.ToLower();
            var sDigits = NormalizeForSearch(search);
            baseQuery = baseQuery.Where(c =>
                c.Name.ToLower().Contains(sLower) ||
                c.PhoneNormalized.Contains(string.IsNullOrEmpty(sDigits) ? sLower : sDigits) ||
                (c.PhoneRaw != null && c.PhoneRaw.Contains(sLower)));
        }

        // Aplica DSL (valida e constrói predicados parametrizados)
        var filtered = ContactFilterBuilder.Apply(baseQuery, filters);

        // Counts por origem DENTRO do escopo filtrado (abas do front)
        var countImport = await filtered.Where(c => c.Origem == "import_csv").CountAsync(ct);
        var countManual = await filtered.Where(c => c.Origem == "manual").CountAsync(ct);
        // webhook_cloudia vive em outra tabela; atualmente não participa do filtro avançado
        // (apenas a aba "webhook" lista leads puros via endpoint legado).
        var countWebhook = await _db.Leads.Where(l => l.TenantId == tenantId).CountAsync(ct);

        // Escopo final por origem
        IQueryable<Contact> scoped = origem switch
        {
            "import_csv" => filtered.Where(c => c.Origem == "import_csv"),
            "manual" => filtered.Where(c => c.Origem == "manual"),
            "webhook_cloudia" => filtered.Where(_ => false), // filtros só valem para Contacts hoje
            _ => filtered, // all = contatos (import + manual); webhook fica fora do DSL por ora
        };

        var totalFiltered = await scoped.CountAsync(ct);

        var ordered = ApplyOrder(scoped, req.OrderBy, req.OrderDir);
        var rows = await ordered
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var totalPages = (int)Math.Ceiling(totalFiltered / (double)pageSize);

        return new ContactSearchResponseDto
        {
            Data = rows.Select(ToDto).ToList(),
            Pagination = new ContactPaginationDto
            {
                Page = page,
                PageSize = pageSize,
                Total = totalFiltered,
                TotalPages = totalPages <= 0 ? 1 : totalPages,
            },
            Counts = new ContactSearchCountsDto
            {
                ImportCsv = countImport,
                Manual = countManual,
                WebhookCloudia = countWebhook,
                All = countImport + countManual + countWebhook,
                Filtered = totalFiltered,
            }
        };
    }

    // ════════════════════════════════════════════════════════════════
    //  Opções dinâmicas para multiselects (GET /contacts/filter-options/{key})
    // ════════════════════════════════════════════════════════════════

    public static readonly HashSet<string> SupportedOptionKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "tags", "etapas", "conexoes",
    };

    public async Task<FilterOptionsResponseDto?> GetFilterOptionsAsync(
        int tenantId,
        string key,
        string? search,
        int limit,
        CancellationToken ct = default)
    {
        if (!SupportedOptionKeys.Contains(key)) return null;

        limit = Math.Clamp(limit, 1, 500);
        var cacheKey = $"filter-opts:{tenantId}:{key}";

        List<string> values;
        if (_cache is not null && _cache.TryGetValue(cacheKey, out List<string>? cached) && cached is not null)
        {
            values = cached;
        }
        else
        {
            values = key.ToLowerInvariant() switch
            {
                "tags" => await LoadTagsAsync(tenantId, ct),
                "etapas" => await LoadEtapasAsync(tenantId, ct),
                "conexoes" => await LoadConexoesAsync(tenantId, ct),
                _ => new()
            };
            _cache?.Set(cacheKey, values, FilterOptionsTtl);
        }

        IEnumerable<string> filtered = values;
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.ToLowerInvariant();
            filtered = filtered.Where(v => v.ToLowerInvariant().Contains(s));
        }

        var options = filtered
            .Take(limit)
            .Select(v => new FilterOptionDto { Value = v, Label = v })
            .ToList();

        return new FilterOptionsResponseDto { Key = key, Options = options };
    }

    private async Task<List<string>> LoadTagsAsync(int tenantId, CancellationToken ct)
    {
        var jsons = await _db.Contacts
            .Where(c => c.TenantId == tenantId && c.TagsJson != null)
            .Select(c => c.TagsJson!)
            .ToListAsync(ct);

        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var j in jsons)
        {
            try
            {
                var list = JsonSerializer.Deserialize<List<string>>(j);
                if (list is null) continue;
                foreach (var t in list)
                    if (!string.IsNullOrWhiteSpace(t)) set.Add(t.Trim());
            }
            catch { /* ignora JSON malformado */ }
        }
        return set.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private async Task<List<string>> LoadEtapasAsync(int tenantId, CancellationToken ct)
    {
        return await _db.Contacts
            .Where(c => c.TenantId == tenantId && c.Etapa != null && c.Etapa != "")
            .Select(c => c.Etapa!)
            .Distinct()
            .OrderBy(x => x)
            .ToListAsync(ct);
    }

    private async Task<List<string>> LoadConexoesAsync(int tenantId, CancellationToken ct)
    {
        return await _db.Contacts
            .Where(c => c.TenantId == tenantId && c.Conexao != null && c.Conexao != "")
            .Select(c => c.Conexao!)
            .Distinct()
            .OrderBy(x => x)
            .ToListAsync(ct);
    }
}
