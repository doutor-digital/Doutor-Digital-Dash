using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.Models;
using LeadAnalytics.Api.Service;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LeadAnalytics.Api.Controllers;

/// <summary>
/// Metas mensais por KPI e unidade.
///
/// Quem define é gestão (super_admin, analista_ti ou manager), não a SDR: meta que a
/// própria pessoa medida ajusta deixa de medir. Quem lê é todo mundo — a meta só funciona
/// se aparecer ao lado do número para quem executa.
///
/// Diferente das Configurações Técnicas, que são nominais: mapear KPI muda o número da
/// rede inteira; definir meta muda só a régua daquela unidade.
/// </summary>
[ApiController]
[Authorize]
[Route("api/config/kpi-goals")]
public class KpiGoalsController(
    AppDbContext db,
    TenantUnitGuard tenantGuard,
    ICurrentUser currentUser) : ControllerBase
{
    private readonly AppDbContext _db = db;
    private readonly TenantUnitGuard _tenantGuard = tenantGuard;
    private readonly ICurrentUser _currentUser = currentUser;

    public record MetaDto(string KpiKey, decimal MetaMensal, string? DefinidaPor, DateTime? AtualizadaEm);

    public record SalvarMetaRequest(string KpiKey, decimal MetaMensal);

    private bool PodeDefinir =>
        _currentUser.IsAdminLevel
        || string.Equals(Roles.Canonical(_currentUser.Role), Roles.Manager, StringComparison.Ordinal);

    /// <summary>Metas da unidade. Qualquer usuário logado com acesso à unidade lê.</summary>
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] int unitId, CancellationToken ct)
    {
        if (await _tenantGuard.EnsureUnitBelongsToTenantAsync(unitId, ct) is { } guard) return guard;

        var metas = await _db.KpiGoals.AsNoTracking()
            .Where(g => g.UnitId == unitId)
            .OrderBy(g => g.KpiKey)
            .Select(g => new MetaDto(g.KpiKey, g.MetaMensal, g.UpdatedByEmail, g.UpdatedAt))
            .ToListAsync(ct);

        return Ok(new { unitId, metas, podeEditar = PodeDefinir });
    }

    /// <summary>
    /// Define (ou apaga) a meta de um KPI. Meta zero ou negativa remove a linha: é assim
    /// que o gestor tira a régua de um KPI sem precisar de uma rota de exclusão.
    /// </summary>
    [HttpPut]
    public async Task<IActionResult> Save(
        [FromQuery] int unitId,
        [FromBody] SalvarMetaRequest req,
        CancellationToken ct)
    {
        if (await _tenantGuard.EnsureUnitBelongsToTenantAsync(unitId, ct) is { } guard) return guard;

        if (!PodeDefinir)
            return StatusCode(StatusCodes.Status403Forbidden, new ProblemDetails
            {
                Title = "Sem permissão para definir metas.",
                Detail = "Meta é decisão de gestão. Fale com a gerência da unidade ou com o analista.",
                Status = StatusCodes.Status403Forbidden,
            });

        var chave = (req.KpiKey ?? string.Empty).Trim();
        if (chave.Length == 0) return BadRequest(new { erro = "kpiKey é obrigatório." });

        var unidade = await _db.Units.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == unitId, ct);
        if (unidade is null) return NotFound(new { erro = "Unidade não encontrada." });

        var existente = await _db.KpiGoals
            .FirstOrDefaultAsync(g => g.UnitId == unitId && g.KpiKey == chave, ct);

        if (req.MetaMensal <= 0)
        {
            if (existente is not null) _db.KpiGoals.Remove(existente);
            await _db.SaveChangesAsync(ct);
            return Ok(new { removida = true, kpiKey = chave });
        }

        var agora = DateTime.UtcNow;
        if (existente is null)
        {
            _db.KpiGoals.Add(new KpiGoal
            {
                UnitId = unitId,
                ClinicId = unidade.ClinicId,
                KpiKey = chave,
                MetaMensal = req.MetaMensal,
                UpdatedByEmail = _currentUser.Email,
                CreatedAt = agora,
                UpdatedAt = agora,
            });
        }
        else
        {
            existente.MetaMensal = req.MetaMensal;
            existente.UpdatedByEmail = _currentUser.Email;
            existente.UpdatedAt = agora;
        }

        await _db.SaveChangesAsync(ct);
        return Ok(new { kpiKey = chave, metaMensal = req.MetaMensal });
    }
}
