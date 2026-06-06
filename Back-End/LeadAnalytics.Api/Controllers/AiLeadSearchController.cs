using LeadAnalytics.Api.Service;
using LeadAnalytics.Api.Service.Ai;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LeadAnalytics.Api.Controllers;

/// <summary>
/// Busca de leads por linguagem natural.
///
/// POST /api/ai/search-leads
/// Body: { query, unitId, limit? }
///
/// O servidor monta o prompt-sistema (com data BRT + domínios reais do Kommo),
/// manda pro gpt-4o-mini, parseia o JSON e executa os filtros no Postgres.
/// V1 cobre só campos diretos do Lead (nome/email/telefone/etapas/tags/valor/
/// data_primeiro_contato). Slugs não mapeados são reportados em ignoredSlugs.
/// </summary>
[ApiController]
[Authorize]
[Route("api/ai")]
public class AiLeadSearchController(
    LeadSearchService search,
    TenantUnitGuard tenantGuard,
    ICurrentUser currentUser,
    ILogger<AiLeadSearchController> logger) : ControllerBase
{
    public record SearchRequest(string Query, int UnitId, int? Limit);

    [HttpPost("search-leads")]
    public async Task<IActionResult> Search(
        [FromBody] SearchRequest body,
        [FromQuery(Name = "tenantId")] int? explicitTenantId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(body.Query))
            return BadRequest(new { error = "Pergunta vazia." });
        if (body.UnitId <= 0)
            return BadRequest(new { error = "unitId obrigatório." });

        var (err, tenantId) = await tenantGuard.ResolveTenantAsync(body.UnitId, ct);
        if (err is not null) return err;
        if (tenantId is null && currentUser.IsSuperAdmin && explicitTenantId > 0)
            tenantId = explicitTenantId;
        if (tenantId is null) return Forbid();

        try
        {
            var result = await search.SearchAsync(tenantId.Value, body.UnitId, body.Query.Trim(), body.Limit ?? 50, ct);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "[ai-search] erro na OpenAI");
            return StatusCode(502, new { error = ex.Message });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[ai-search] erro inesperado");
            return StatusCode(500, new { error = $"{ex.GetType().Name}: {ex.Message}" });
        }
    }
}
