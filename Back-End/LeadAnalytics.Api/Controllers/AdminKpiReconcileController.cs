using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.Service;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LeadAnalytics.Api.Controllers;

/// <summary>
/// Reconciliação em massa dos KPIs contra o relatório oficial da clínica (CSV).
/// Caso de uso: SDR moveu lead errado e a chefe quer o dashboard batendo com o
/// relatório real até o fim do dia. Em vez de corrigir lead por lead na página
/// de auditoria, sobe-se o CSV e o serviço:
///   • Corrige a data efetiva da transição no LeadStageHistory (CorrectedChangedAt).
///   • Marca como "não contar" (kpi_exclusions) os leads que estão na etapa
///     no banco mas NÃO no CSV.
///
/// Todos os endpoints aceitam dryRun=true (default) — devolve o que VAI mudar
/// sem aplicar. Confira o resultado e dispare com dryRun=false pra aplicar.
///
/// Match: telefone (variantes 8/9/10/11 dígitos) → fallback por nome + data inline
/// no Kommo (ex.: "Edileusa 01/02/25"). Mesma estratégia do Cloudia CSV import.
/// </summary>
[ApiController]
[AllowAnonymous] // alinhado com AdminKpiExclusionsController (admin-only é resolvido por unidade)
[Route("api/admin/kpi-reconcile")]
public class AdminKpiReconcileController(
    AppDbContext db,
    KpiReconcileService svc,
    ILogger<AdminKpiReconcileController> logger) : ControllerBase
{
    [HttpPost("tratamentos")]
    [RequestSizeLimit(50_000_000)] // 50MB de CSV cobre folgado as 3 unidades
    public async Task<IActionResult> ReconcileTratamentos(
        [FromQuery] int unitId,
        [FromQuery] bool dryRun = true,
        IFormFile? file = null,
        CancellationToken ct = default)
    {
        var (err, tenantId) = await ResolveAsync(unitId, ct);
        if (err is not null) return err;
        if (file is null || file.Length == 0) return BadRequest(new { error = "envie o arquivo CSV no campo 'file' (multipart)" });

        await using var stream = file.OpenReadStream();
        var result = await svc.ReconcileTratamentosAsync(unitId, tenantId!.Value, stream, dryRun, ct);
        logger.LogInformation("[kpi-reconcile/trat] file={File} unit={Unit} dryRun={Dry} → corr={Corr} excl={Excl}",
            file.FileName, unitId, dryRun, result.DatesCorrected, result.ExclusionsAdded);
        return Ok(result);
    }

    [HttpPost("agendados")]
    [RequestSizeLimit(50_000_000)]
    public async Task<IActionResult> ReconcileAgendados(
        [FromQuery] int unitId,
        [FromQuery] bool dryRun = true,
        IFormFile? file = null,
        CancellationToken ct = default)
    {
        var (err, tenantId) = await ResolveAsync(unitId, ct);
        if (err is not null) return err;
        if (file is null || file.Length == 0) return BadRequest(new { error = "envie o arquivo CSV no campo 'file' (multipart)" });

        await using var stream = file.OpenReadStream();
        var result = await svc.ReconcileAgendadosAsync(unitId, tenantId!.Value, stream, dryRun, ct);
        logger.LogInformation("[kpi-reconcile/ag] file={File} unit={Unit} dryRun={Dry} → corr={Corr} excl={Excl}",
            file.FileName, unitId, dryRun, result.DatesCorrected, result.ExclusionsAdded);
        return Ok(result);
    }

    [HttpPost("compareceu")]
    [RequestSizeLimit(50_000_000)]
    public async Task<IActionResult> ReconcileCompareceu(
        [FromQuery] int unitId,
        [FromQuery] bool dryRun = true,
        IFormFile? file = null,
        CancellationToken ct = default)
    {
        var (err, tenantId) = await ResolveAsync(unitId, ct);
        if (err is not null) return err;
        if (file is null || file.Length == 0) return BadRequest(new { error = "envie o arquivo CSV no campo 'file' (multipart)" });

        await using var stream = file.OpenReadStream();
        var result = await svc.ReconcileCompareceuAsync(unitId, tenantId!.Value, stream, dryRun, ct);
        logger.LogInformation("[kpi-reconcile/comp] file={File} unit={Unit} dryRun={Dry} → att={Att}",
            file.FileName, unitId, dryRun, result.AttendanceMarked);
        return Ok(result);
    }

    private async Task<(IActionResult? Error, int? TenantId)> ResolveAsync(int unitId, CancellationToken ct)
    {
        var unit = await db.Units.AsNoTracking()
            .Where(u => u.Id == unitId)
            .Select(u => new { u.Id, u.ClinicId })
            .FirstOrDefaultAsync(ct);
        if (unit is null) return (NotFound(new { error = "unit não encontrada" }), null);
        return (null, unit.ClinicId);
    }
}
