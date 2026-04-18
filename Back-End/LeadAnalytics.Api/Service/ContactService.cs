using System.Text.Json;
using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.DTOs.Response;
using Microsoft.EntityFrameworkCore;

namespace LeadAnalytics.Api.Service;

public class ContactService(AppDbContext db)
{
    private readonly AppDbContext _db = db;

    public async Task<ContactsListResponseDto> ListAsync(
        int tenantId,
        string origem = "all",
        string? search = null,
        int page = 1,
        int pageSize = 50,
        string orderBy = "created_at",
        string orderDir = "desc",
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var normalized = string.IsNullOrWhiteSpace(search)
            ? null
            : NormalizeForSearch(search);

        // ── COUNTS (sempre ignorando filtro de origem para mostrar totais) ──
        var importTotal = await _db.Contacts
            .Where(c => c.TenantId == tenantId)
            .CountAsync(ct);

        var webhookTotal = await _db.Leads
            .Where(l => l.TenantId == tenantId)
            .CountAsync(ct);

        var counts = new ContactCountsDto
        {
            ImportCsv = importTotal,
            WebhookCloudia = webhookTotal,
            All = importTotal + webhookTotal,
        };

        // ── DATA (com filtros aplicados) ──
        var items = new List<ContactDto>();
        var total = 0;

        if (origem == "import_csv" || origem == "all")
        {
            var q = _db.Contacts.AsNoTracking()
                .Where(c => c.TenantId == tenantId);

            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.ToLower();
                q = q.Where(c =>
                    c.Name.ToLower().Contains(s) ||
                    c.PhoneNormalized.Contains(normalized ?? s) ||
                    (c.PhoneRaw != null && c.PhoneRaw.Contains(s)));
            }

            var count = await q.CountAsync(ct);
            total += count;

            if (origem == "import_csv")
            {
                var rows = await ApplyOrder(q, orderBy, orderDir)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync(ct);

                items.AddRange(rows.Select(ToDto));
            }
        }

        if (origem == "webhook_cloudia" || origem == "all")
        {
            var q = _db.Leads.AsNoTracking()
                .Where(l => l.TenantId == tenantId);

            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.ToLower();
                q = q.Where(l =>
                    l.Name.ToLower().Contains(s) ||
                    l.Phone.Contains(normalized ?? s));
            }

            var count = await q.CountAsync(ct);
            total += count;

            if (origem == "webhook_cloudia")
            {
                var rows = await ApplyLeadOrder(q, orderBy, orderDir)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync(ct);

                items.AddRange(rows.Select(ToDtoFromLead));
            }
        }

        // ── "all" exige merge + ordenação em memória ──
        if (origem == "all")
        {
            var contactsQ = _db.Contacts.AsNoTracking()
                .Where(c => c.TenantId == tenantId);
            var leadsQ = _db.Leads.AsNoTracking()
                .Where(l => l.TenantId == tenantId);

            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.ToLower();
                contactsQ = contactsQ.Where(c =>
                    c.Name.ToLower().Contains(s) ||
                    c.PhoneNormalized.Contains(normalized ?? s) ||
                    (c.PhoneRaw != null && c.PhoneRaw.Contains(s)));
                leadsQ = leadsQ.Where(l =>
                    l.Name.ToLower().Contains(s) ||
                    l.Phone.Contains(normalized ?? s));
            }

            // Limitar por perf — pega até page*pageSize de cada lado + merge
            var take = page * pageSize + pageSize;
            var contactList = await contactsQ
                .OrderByDescending(c => c.LastMessageAt ?? c.CreatedAt)
                .Take(take)
                .ToListAsync(ct);
            var leadList = await leadsQ
                .OrderByDescending(l => l.UpdatedAt)
                .Take(take)
                .ToListAsync(ct);

            var merged = contactList.Select(ToDto)
                .Concat(leadList.Select(ToDtoFromLead))
                .OrderByDescending(c => c.LastMessageAt ?? DateTime.MinValue)
                .ToList();

            total = await contactsQ.CountAsync(ct) + await leadsQ.CountAsync(ct);

            items = merged
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();
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

    public async Task<ContactDetailDto?> GetByIdAsync(
        int tenantId,
        string id,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;

        // Aceita "c_123", "l_123" ou "123" puro (tenta Contact, depois Lead).
        string prefix;
        string numeric;
        if (id.StartsWith("c_", StringComparison.OrdinalIgnoreCase))
        {
            prefix = "c";
            numeric = id[2..];
        }
        else if (id.StartsWith("l_", StringComparison.OrdinalIgnoreCase))
        {
            prefix = "l";
            numeric = id[2..];
        }
        else
        {
            prefix = "auto";
            numeric = id;
        }

        if (!int.TryParse(numeric, out var numId)) return null;

        if (prefix == "c" || prefix == "auto")
        {
            var contact = await _db.Contacts
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == numId && c.TenantId == tenantId, ct);
            if (contact is not null) return ToDetail(contact);
            if (prefix == "c") return null;
        }

        // prefix == "l" OU fallback "auto"
        var lead = await _db.Leads
            .AsNoTracking()
            .Include(l => l.Unit)
            .Include(l => l.Attendant)
            .FirstOrDefaultAsync(l => l.Id == numId && l.TenantId == tenantId, ct);

        return lead is null ? null : ToDetail(lead);
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
}
