using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.Service;
using LeadAnalytics.Api.Service.Ai;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LeadAnalytics.Api.Controllers;

/// <summary>
/// A conversa que a atendente virtual teve com o lead, e a leitura dela.
///
/// - <c>GET  /api/leads/{id}/conversa</c>          → mensagens, na ordem em que aconteceram.
/// - <c>POST /api/leads/{id}/conversa/analise</c>  → GPT no papel de supervisor de SDR.
///
/// A análise custa chamada de API, então fica guardada por 6h e só é refeita quando
/// chega mensagem nova — ou quando o usuário pede de novo com <c>?forcar=true</c>.
/// </summary>
[ApiController]
[Authorize]
[Route("api/leads/{id:int}/conversa")]
public class ConversaLeadController(
    AppDbContext db,
    AnaliseConversaService analise,
    TenantUnitGuard tenantGuard,
    ILogger<ConversaLeadController> logger) : ControllerBase
{
    /// <summary>Mensagens da conversa do lead com a I.A., em ordem cronológica.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(ConversaDoLeadDto), 200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> Get(int id, CancellationToken ct = default)
    {
        if (tenantGuard.RequireTenant(out var doUsuario) is { } denied) return denied;
        var (erro, tenantId) = await ResolverTenantDoLeadAsync(id, doUsuario, ct);
        if (erro is not null) return erro;

        var conversa = await analise.GetConversaAsync(id, tenantId, ct);
        return conversa is null
            ? NotFound(new { message = "Este lead não tem conversa registrada com a I.A." })
            : Ok(conversa);
    }

    /// <summary>Leitura da conversa por uma I.A. no papel de supervisor de SDR.</summary>
    [HttpPost("analise")]
    [ProducesResponseType(typeof(AnaliseConversaDto), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> Analisar(
        int id, [FromQuery] bool forcar = false, CancellationToken ct = default)
    {
        if (tenantGuard.RequireTenant(out var doUsuario) is { } denied) return denied;
        var (erro, tenantId) = await ResolverTenantDoLeadAsync(id, doUsuario, ct);
        if (erro is not null) return erro;

        try
        {
            return Ok(await analise.AnalisarAsync(id, tenantId, forcar, ct));
        }
        catch (InvalidOperationException ex)
        {
            // Falta de conversa ou de chave é situação esperada, não defeito:
            // o front mostra a frase como está, então ela precisa explicar o passo.
            return BadRequest(new { message = ex.Message });
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Análise de conversa falhou na OpenAI | lead={Lead}", id);
            return StatusCode(502, new { message = "A OpenAI não respondeu. Tente de novo em instantes." });
        }
    }

    /// <summary>
    /// O tenant sai do próprio lead, não do JWT: o super_admin não tem tenant no
    /// token e mesmo assim precisa abrir o lead. Para os demais, o tenant do token
    /// tem que bater com o do lead — se não bater, o lead simplesmente não existe
    /// para quem perguntou.
    /// </summary>
    private async Task<(IActionResult? Erro, int TenantId)> ResolverTenantDoLeadAsync(
        int leadId, int? tenantDoUsuario, CancellationToken ct)
    {
        var doLead = await db.Leads.AsNoTracking()
            .Where(l => l.Id == leadId)
            .Select(l => (int?)l.TenantId)
            .FirstOrDefaultAsync(ct);

        if (doLead is null || (tenantDoUsuario is int t && t != doLead))
            return (NotFound(new { message = "Lead não encontrado." }), 0);

        return (null, doLead.Value);
    }
}
