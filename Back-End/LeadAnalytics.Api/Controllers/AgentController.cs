using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.Service;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LeadAnalytics.Api.Controllers;

/// <summary>
/// Dados da I.A. (agente-Dt) para o dashboard: KPIs, lista de conversas e detalhe
/// com as mensagens. Tudo isolado por tenant via <see cref="TenantUnitGuard"/>.
/// </summary>
[ApiController]
[Authorize]
[Route("api/agent")]
public class AgentController(
    AppDbContext db,
    TenantUnitGuard tenantGuard) : ControllerBase
{
    private readonly AppDbContext _db = db;
    private readonly TenantUnitGuard _tenantGuard = tenantGuard;

    /// <summary>KPIs gerais da I.A. no período (cards do topo do dashboard).</summary>
    [HttpGet("overview")]
    public async Task<IActionResult> Overview(
        [FromQuery] int? unitId,
        [FromQuery] DateTime? dateFrom,
        [FromQuery] DateTime? dateTo,
        CancellationToken ct = default)
    {
        var (error, tenantId) = await _tenantGuard.ResolveTenantAsync(unitId, ct);
        if (error is not null) return error;

        var q = _db.AgentConversations.AsNoTracking().AsQueryable();
        if (tenantId.HasValue) q = q.Where(c => c.TenantId == tenantId);
        if (unitId.HasValue) q = q.Where(c => c.UnitId == unitId);
        if (dateFrom.HasValue) q = q.Where(c => c.StartedAt >= dateFrom);
        if (dateTo.HasValue) q = q.Where(c => c.StartedAt <= dateTo);

        var agg = await q
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Total = g.Count(),
                Active = g.Count(c => c.Status == "active"),
                Closed = g.Count(c => c.Status == "closed"),
                Handoff = g.Count(c => c.HandedOff || c.Status == "handoff"),
                Messages = g.Sum(c => c.MessageCount),
                LinkedLead = g.Count(c => c.LeadId != null),
                LinkedContact = g.Count(c => c.ContactId != null),
                TokensIn = g.Sum(c => (long?)c.TokensIn) ?? 0,
                TokensOut = g.Sum(c => (long?)c.TokensOut) ?? 0,
            })
            .FirstOrDefaultAsync(ct);

        var total = agg?.Total ?? 0;

        // Série diária de conversas iniciadas (gráfico).
        var byDayRaw = await q.Select(c => c.StartedAt).ToListAsync(ct);
        var byDay = byDayRaw
            .GroupBy(d => d.Date)
            .Select(g => new AgentDayPointDto { Date = g.Key, Count = g.Count() })
            .OrderBy(p => p.Date)
            .ToList();

        return Ok(new AgentOverviewDto
        {
            TotalConversations = total,
            ActiveConversations = agg?.Active ?? 0,
            ClosedConversations = agg?.Closed ?? 0,
            HandoffConversations = agg?.Handoff ?? 0,
            TotalMessages = agg?.Messages ?? 0,
            AvgMessagesPerConversation = total > 0 ? Math.Round((double)(agg!.Messages) / total, 1) : 0,
            HandoffRate = total > 0 ? Math.Round(100.0 * (agg!.Handoff) / total, 1) : 0,
            LeadsLinked = agg?.LinkedLead ?? 0,
            ContactsLinked = agg?.LinkedContact ?? 0,
            TokensIn = agg?.TokensIn ?? 0,
            TokensOut = agg?.TokensOut ?? 0,
            SeriesByDay = byDay,
        });
    }

    /// <summary>Lista paginada de conversas, ordenada pela última mensagem.</summary>
    [HttpGet("conversations")]
    public async Task<IActionResult> List(
        [FromQuery] int? unitId,
        [FromQuery] string? status,
        [FromQuery] string? search,
        [FromQuery] DateTime? dateFrom,
        [FromQuery] DateTime? dateTo,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        if (page < 1) page = 1;
        if (pageSize is < 1 or > 200) pageSize = 50;

        var (error, tenantId) = await _tenantGuard.ResolveTenantAsync(unitId, ct);
        if (error is not null) return error;

        var q = _db.AgentConversations.AsNoTracking().AsQueryable();
        if (tenantId.HasValue) q = q.Where(c => c.TenantId == tenantId);
        if (unitId.HasValue) q = q.Where(c => c.UnitId == unitId);
        if (dateFrom.HasValue) q = q.Where(c => c.StartedAt >= dateFrom);
        if (dateTo.HasValue) q = q.Where(c => c.StartedAt <= dateTo);

        var st = status?.Trim().ToLowerInvariant();
        if (st == "handoff") q = q.Where(c => c.HandedOff || c.Status == "handoff");
        else if (st is "active" or "closed") q = q.Where(c => c.Status == st);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(c =>
                (c.ContactName != null && EF.Functions.ILike(c.ContactName, $"%{s}%")) ||
                (c.ContactPhone != null && c.ContactPhone.Contains(s)) ||
                (c.PhoneNormalized != null && c.PhoneNormalized.Contains(s)) ||
                (c.Summary != null && EF.Functions.ILike(c.Summary, $"%{s}%")));
        }

        var total = await q.CountAsync(ct);

        var items = await q
            .OrderByDescending(c => c.LastMessageAt ?? c.StartedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(c => new AgentConversationListItemDto
            {
                Id = c.Id,
                ExternalId = c.ExternalId,
                AgentName = c.AgentName,
                Channel = c.Channel,
                ContactName = c.ContactName,
                ContactPhone = c.ContactPhone,
                Status = c.Status,
                HandedOff = c.HandedOff,
                MessageCount = c.MessageCount,
                Intent = c.Intent,
                Sentiment = c.Sentiment,
                Summary = c.Summary,
                StartedAt = c.StartedAt,
                LastMessageAt = c.LastMessageAt,
                LeadId = c.LeadId,
                ContactId = c.ContactId,
            })
            .ToListAsync(ct);

        return Ok(new AgentConversationListDto
        {
            Page = page,
            PageSize = pageSize,
            Total = total,
            Items = items,
        });
    }

    /// <summary>Detalhe de uma conversa + todas as mensagens em ordem cronológica.</summary>
    [HttpGet("conversations/{id:int}")]
    public async Task<IActionResult> Detail(int id, [FromQuery] int? unitId, CancellationToken ct = default)
    {
        var (error, tenantId) = await _tenantGuard.ResolveTenantAsync(unitId, ct);
        if (error is not null) return error;

        var conv = await _db.AgentConversations.AsNoTracking()
            .Where(c => c.Id == id)
            .Where(c => !tenantId.HasValue || c.TenantId == tenantId)
            .Select(c => new AgentConversationDetailDto
            {
                Id = c.Id,
                ExternalId = c.ExternalId,
                AgentName = c.AgentName,
                Channel = c.Channel,
                ContactName = c.ContactName,
                ContactPhone = c.ContactPhone,
                Status = c.Status,
                HandedOff = c.HandedOff,
                HandoffAt = c.HandoffAt,
                MessageCount = c.MessageCount,
                Intent = c.Intent,
                Sentiment = c.Sentiment,
                Summary = c.Summary,
                TokensIn = c.TokensIn,
                TokensOut = c.TokensOut,
                StartedAt = c.StartedAt,
                EndedAt = c.EndedAt,
                FirstMessageAt = c.FirstMessageAt,
                LastMessageAt = c.LastMessageAt,
                LeadId = c.LeadId,
                ContactId = c.ContactId,
                Messages = c.Messages
                    .OrderBy(m => m.SentAt).ThenBy(m => m.Id)
                    .Select(m => new AgentMessageDtoOut
                    {
                        Id = m.Id,
                        Role = m.Role,
                        Content = m.Content,
                        SentAt = m.SentAt,
                        ToolName = m.ToolName,
                    })
                    .ToList(),
            })
            .FirstOrDefaultAsync(ct);

        if (conv is null) return NotFound(new ProblemDetails { Title = "Conversa não encontrada", Status = 404 });

        return Ok(conv);
    }
}

// ─── DTOs ───────────────────────────────────────────────────────────────────

public class AgentOverviewDto
{
    public int TotalConversations { get; set; }
    public int ActiveConversations { get; set; }
    public int ClosedConversations { get; set; }
    public int HandoffConversations { get; set; }
    public int TotalMessages { get; set; }
    public double AvgMessagesPerConversation { get; set; }
    public double HandoffRate { get; set; }
    public int LeadsLinked { get; set; }
    public int ContactsLinked { get; set; }
    public long TokensIn { get; set; }
    public long TokensOut { get; set; }
    public List<AgentDayPointDto> SeriesByDay { get; set; } = new();
}

public class AgentDayPointDto
{
    public DateTime Date { get; set; }
    public int Count { get; set; }
}

public class AgentConversationListDto
{
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int Total { get; set; }
    public List<AgentConversationListItemDto> Items { get; set; } = new();
}

public class AgentConversationListItemDto
{
    public int Id { get; set; }
    public string ExternalId { get; set; } = null!;
    public string? AgentName { get; set; }
    public string? Channel { get; set; }
    public string? ContactName { get; set; }
    public string? ContactPhone { get; set; }
    public string Status { get; set; } = null!;
    public bool HandedOff { get; set; }
    public int MessageCount { get; set; }
    public string? Intent { get; set; }
    public string? Sentiment { get; set; }
    public string? Summary { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? LastMessageAt { get; set; }
    public int? LeadId { get; set; }
    public int? ContactId { get; set; }
}

public class AgentConversationDetailDto : AgentConversationListItemDto
{
    public DateTime? HandoffAt { get; set; }
    public int? TokensIn { get; set; }
    public int? TokensOut { get; set; }
    public DateTime? EndedAt { get; set; }
    public DateTime? FirstMessageAt { get; set; }
    public List<AgentMessageDtoOut> Messages { get; set; } = new();
}

public class AgentMessageDtoOut
{
    public int Id { get; set; }
    public string Role { get; set; } = null!;
    public string? Content { get; set; }
    public DateTime SentAt { get; set; }
    public string? ToolName { get; set; }
}
