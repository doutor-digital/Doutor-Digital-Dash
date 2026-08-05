using LeadAnalytics.Api.Service;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LeadAnalytics.Api.Controllers;

/// <summary>
/// Qualidade do preenchimento dos cartões.
///
/// Existe porque o número do dashboard é tão bom quanto o cartão que a SDR preencheu, e
/// hoje a origem está preenchida em ~30% dos leads. Sem uma tela que mostre isso, a
/// conversa vira "o dashboard está errado" quando o dashboard está certo e o campo está
/// vazio.
///
/// Somente leitura. Corrigir cartão em massa mexe no CRM de produção e é decisão de
/// gente — o painel mostra o que dá para corrigir e quantos, não aperta o botão sozinho.
/// </summary>
[ApiController]
[Authorize]
[Route("api/qualidade")]
public class QualidadeController(
    QualidadeService qualidade,
    TenantUnitGuard tenantGuard) : ControllerBase
{
    private readonly QualidadeService _qualidade = qualidade;
    private readonly TenantUnitGuard _tenantGuard = tenantGuard;

    /// <summary>
    /// Preenchimento por campo, incoerências e distribuição por responsável.
    /// Padrão: últimos 30 dias.
    /// </summary>
    [HttpGet("preenchimento")]
    public async Task<IActionResult> Preenchimento(
        [FromQuery] int? unitId,
        [FromQuery] DateTime? de,
        [FromQuery] DateTime? ate,
        CancellationToken ct = default)
    {
        var (error, tenantId) = await _tenantGuard.ResolveTenantAsync(unitId, ct);
        if (error is not null) return error;
        if (tenantId is not int tid) return Forbid();

        var fim = (ate ?? DateTime.UtcNow).Date.AddDays(1).AddTicks(-1);
        var inicio = (de ?? fim.AddDays(-30)).Date;
        if (fim < inicio)
            return BadRequest(new ProblemDetails { Title = "Período inválido: 'ate' anterior a 'de'.", Status = 400 });

        var dto = await _qualidade.GetAsync(
            tid, unitId,
            DateTime.SpecifyKind(inicio, DateTimeKind.Utc),
            DateTime.SpecifyKind(fim, DateTimeKind.Utc),
            ct);

        return Ok(dto);
    }
}
