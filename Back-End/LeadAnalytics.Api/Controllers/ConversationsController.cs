using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.DTOs.Conversations;
using LeadAnalytics.Api.Service;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LeadAnalytics.Api.Controllers;

/// <summary>
/// Endpoints que alimentam o dashboard "Conversas &amp; Atendimento" (front /conversas).
///
/// <para><b>Fontes unidas (sem n8n)</b></para>
/// <list type="bullet">
/// <item><c>agent_messages</c> — conversas conduzidas pelo agente-Dt via webhook próprio.</item>
/// <item><b>Kommo Talks</b> (REST v4) — metadados de TODA conversa da Kommo, mesmo 100% humana.</item>
/// <item><b>Kommo Notes</b> (REST v4) — texto das mensagens de chat (service_message / extended).</item>
/// </list>
///
/// Kommo só é consultada quando <c>unitId</c> é informado E a unit tem
/// <c>KommoAccessToken</c>. Falhas no Kommo são silenciadas (log warning) pra
/// não derrubar o painel — agent_messages continua respondendo.
/// </summary>
[ApiController]
[Authorize]
[Route("api/conversations")]
public class ConversationsController(
    AppDbContext db,
    TenantUnitGuard tenantGuard,
    KommoConversationsImporter kommoImporter,
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

        // ─── Union com Kommo (Talks + Notes) quando unit explicitamente fornecida ──
        if (unitId.HasValue)
        {
            var unit = await db.Units
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == unitId.Value, ct);

            if (unit is not null
                && !string.IsNullOrWhiteSpace(unit.KommoSubdomain)
                && !string.IsNullOrWhiteSpace(unit.KommoAccessToken))
            {
                var kommoEvents = await kommoImporter.ImportAsync(unit, from, to, ct);

                // Filtros (campanha/agente) aplicados também aos dados Kommo
                var kommoFiltered = kommoEvents.Where(e =>
                {
                    if (!string.IsNullOrWhiteSpace(campanha)
                        && !campanha.Equals("todas", StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(e.Campanha, campanha, StringComparison.OrdinalIgnoreCase))
                        return false;
                    if (!string.IsNullOrWhiteSpace(agente)
                        && !agente.Equals("todos", StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(e.Agente, agente, StringComparison.OrdinalIgnoreCase))
                        return false;
                    return true;
                });

                items.AddRange(kommoFiltered);
            }
        }

        // Ordena por timestamp pra UI agregar corretamente
        items = items.OrderBy(e => e.Timestamp).Take(limit).ToList();

        logger.LogInformation(
            "[conversations] tenant={Tenant} unit={Unit} from={From} to={To} → {Count} eventos (union)",
            tenantId, unitId, from, to, items.Count);

        return Ok(new MessagesListResponse { Items = items, NextCursor = null });
    }
}
