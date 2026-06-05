using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.DTOs.Conversations;
using LeadAnalytics.Api.Service;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LeadAnalytics.Api.Controllers;

/// <summary>
/// Endpoints que alimentam o dashboard "Conversas & Atendimento" do front
/// (rota <c>/conversas</c>). Fonte: tabela <c>agent_messages</c> — webhook próprio
/// do agente-Dt. <b>Não passa por n8n.</b>
///
/// Limitação conhecida: cobre só conversas em que a I.A. (agente-Dt) atuou;
/// atendimento 100% humano feito direto no Kommo não aparece aqui — exigiria
/// integração com a Chats API (Amojo) da Kommo, ainda não implementada.
/// </summary>
[ApiController]
[Authorize]
[Route("api/conversations")]
public class ConversationsController(
    AppDbContext db,
    TenantUnitGuard tenantGuard,
    ILogger<ConversationsController> logger) : ControllerBase
{
    private const int MaxLimit = 10_000;
    private const int DefaultLimit = 5_000;

    /// <summary>
    /// Lista eventos de mensagem dentro do intervalo, no formato consumido pelo
    /// front (<c>MessageEvent</c>). A página agrega no cliente (1ª resposta,
    /// sem-resposta, heatmap, tabela por agente, eficiência por campanha).
    /// </summary>
    [HttpGet("messages")]
    public async Task<IActionResult> ListMessages(
        [FromQuery] int? unitId,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] string? campanha,
        [FromQuery] string? agente,
        [FromQuery] int limit = DefaultLimit,
        CancellationToken ct = default)
    {
        if (from.HasValue && to.HasValue && to.Value < from.Value)
            return BadRequest(new ProblemDetails { Title = "to deve ser >= from", Status = 400 });

        var (err, tenantId) = await tenantGuard.ResolveTenantAsync(unitId, ct);
        if (err is not null) return err;

        limit = Math.Clamp(limit, 1, MaxLimit);

        var q = db.AgentMessages
            .AsNoTracking()
            .Include(m => m.Conversation)
                .ThenInclude(c => c.Lead)
            .Where(m => m.Conversation.TenantId == (tenantId ?? m.Conversation.TenantId));

        if (unitId.HasValue)
            q = q.Where(m => m.Conversation.UnitId == unitId.Value);

        if (from.HasValue)
            q = q.Where(m => m.SentAt >= from.Value);

        if (to.HasValue)
            q = q.Where(m => m.SentAt <= to.Value);

        if (!string.IsNullOrWhiteSpace(campanha) && !campanha.Equals("todas", StringComparison.OrdinalIgnoreCase))
            q = q.Where(m => m.Conversation.Lead != null && m.Conversation.Lead.Campaign == campanha);

        if (!string.IsNullOrWhiteSpace(agente) && !agente.Equals("todos", StringComparison.OrdinalIgnoreCase))
            q = q.Where(m => m.Conversation.AgentName == agente);

        var rows = await q
            .OrderBy(m => m.SentAt)
            .Take(limit)
            .Select(m => new
            {
                m.Id,
                m.ExternalId,
                m.Role,
                m.SentAt,
                ConversationId = m.AgentConversationId,
                AgentName = m.Conversation.AgentName,
                LeadId = m.Conversation.LeadId,
                LeadExternalId = m.Conversation.ExternalId,
                Campaign = m.Conversation.Lead != null ? m.Conversation.Lead.Campaign : null,
            })
            .ToListAsync(ct);

        var items = rows.Select(r => new MessageEventDto
        {
            MensagemId = string.IsNullOrWhiteSpace(r.ExternalId) ? r.Id.ToString() : r.ExternalId!,
            LeadId = r.LeadId.HasValue ? r.LeadId.Value.ToString() : r.LeadExternalId,
            Direcao = r.Role == "user" ? "entrada" : "saida",
            Timestamp = DateTime.SpecifyKind(r.SentAt, DateTimeKind.Utc),
            Tipo = "texto",
            Agente = r.Role == "user" ? null : r.AgentName,
            Campanha = string.IsNullOrWhiteSpace(r.Campaign) ? "—" : r.Campaign!,
        }).ToList();

        logger.LogInformation(
            "[conversations] tenant={Tenant} unit={Unit} from={From} to={To} → {Count} eventos",
            tenantId, unitId, from, to, items.Count);

        return Ok(new MessagesListResponse { Items = items, NextCursor = null });
    }
}
