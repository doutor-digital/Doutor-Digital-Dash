using LeadAnalytics.Api.DTOs.Sdr;
using LeadAnalytics.Api.Service;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LeadAnalytics.Api.Controllers;

// ───────────────────────────────────────────────────────────────────────────
// /api/sdr/leads
// ───────────────────────────────────────────────────────────────────────────

[ApiController]
[Authorize]
[Route("api/sdr/leads")]
public class SdrLeadsController(
    SdrLeadService leadService,
    TenantUnitGuard tenantGuard,
    ILogger<SdrLeadsController> logger) : ControllerBase
{
    private readonly SdrLeadService _leadService = leadService;
    private readonly TenantUnitGuard _tenantGuard = tenantGuard;
    private readonly ILogger<SdrLeadsController> _logger = logger;

    /// <summary>
    /// Lista leads. Use status="pendente_revisao" para a tela de revisão
    /// e status="aprovado" para a tela /sdr/leads-aprovados.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(List<SdrLeadResponseDto>), 200)]
    public async Task<IActionResult> List(
        [FromQuery] string? status,
        [FromQuery] string? source,
        [FromQuery] string? search,
        [FromQuery] int? unitId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 100,
        CancellationToken ct = default)
    {
        if (_tenantGuard.RequireTenant(out var tenantId) is { } denied) return denied;
        if (tenantId is null) return BadRequest(new ProblemDetails { Title = "tenant_id ausente", Status = 400 });
        var rows = await _leadService.ListAsync(tenantId.Value, status, source, search, unitId, page, pageSize, ct);
        return Ok(rows);
    }

    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(SdrLeadResponseDto), 200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetById(int id, CancellationToken ct = default)
    {
        if (_tenantGuard.RequireTenant(out var tenantId) is { } denied) return denied;
        if (tenantId is null) return BadRequest();
        var lead = await _leadService.GetByIdAsync(tenantId.Value, id, ct);
        return lead is null ? NotFound() : Ok(lead);
    }

    [HttpPost]
    [ProducesResponseType(typeof(SdrLeadResponseDto), 201)]
    [ProducesResponseType(400)]
    public async Task<IActionResult> Create([FromBody] SdrLeadCreateDto dto, CancellationToken ct = default)
    {
        if (_tenantGuard.RequireTenant(out var tenantId) is { } denied) return denied;
        if (tenantId is null) return BadRequest();
        try
        {
            var lead = await _leadService.CreateManualAsync(tenantId.Value, dto, ct);
            return CreatedAtAction(nameof(GetById), new { id = lead.Id }, lead);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new ProblemDetails { Title = ex.Message, Status = 400 });
        }
    }

    [HttpPut("{id:int}")]
    [ProducesResponseType(typeof(SdrLeadResponseDto), 200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> Update(int id, [FromBody] SdrLeadUpdateDto dto, CancellationToken ct = default)
    {
        if (_tenantGuard.RequireTenant(out var tenantId) is { } denied) return denied;
        if (tenantId is null) return BadRequest();
        try
        {
            var lead = await _leadService.UpdateAsync(tenantId.Value, id, dto, ct);
            return lead is null ? NotFound() : Ok(lead);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new ProblemDetails { Title = ex.Message, Status = 400 });
        }
    }

    /// <summary>
    /// Promove (aprova) ou rejeita uma revisão. Endpoint principal do fluxo CRM.
    /// Body: { action: "approve" | "reject", rejectionReason?: string, patch?: SdrLeadUpdateDto }
    /// </summary>
    [HttpPost("{id:int}/review")]
    [ProducesResponseType(typeof(SdrLeadResponseDto), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> Review(int id, [FromBody] SdrLeadReviewActionDto dto, CancellationToken ct = default)
    {
        if (_tenantGuard.RequireTenant(out var tenantId) is { } denied) return denied;
        if (tenantId is null) return BadRequest();
        try
        {
            var lead = await _leadService.ReviewAsync(tenantId.Value, id, dto, ct);
            return lead is null ? NotFound() : Ok(lead);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ProblemDetails { Title = ex.Message, Status = 400 });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new ProblemDetails { Title = ex.Message, Status = 400 });
        }
    }

    [HttpDelete("{id:int}")]
    [ProducesResponseType(204)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct = default)
    {
        if (_tenantGuard.RequireTenant(out var tenantId) is { } denied) return denied;
        if (tenantId is null) return BadRequest();
        var ok = await _leadService.DeleteAsync(tenantId.Value, id, ct);
        return ok ? NoContent() : NotFound();
    }

    /// <summary>
    /// Backfill: olha a tabela <c>leads</c> (legada, populada pelo webhook antigo) e cria
    /// <c>sdr_leads</c> correspondentes que ainda não existam — pra não perder leads que
    /// chegaram antes do fluxo de revisão SDR existir. Idempotente.
    /// </summary>
    [HttpPost("sync-from-cloudia")]
    [ProducesResponseType(typeof(SdrLeadService.SyncSummary), 200)]
    public async Task<IActionResult> SyncFromCloudia(CancellationToken ct = default)
    {
        if (_tenantGuard.RequireTenant(out var tenantId) is { } denied) return denied;
        if (tenantId is null) return BadRequest();
        var summary = await _leadService.SyncFromLegacyLeadsAsync(tenantId.Value, ct);
        _logger.LogInformation(
            "🔄 SDR sync (tenant={Tenant}): created={Created} skipped={Skipped} failed={Failed}",
            tenantId, summary.Created, summary.Skipped, summary.Failed);
        return Ok(summary);
    }
}

// ───────────────────────────────────────────────────────────────────────────
// /api/sdr/consultas
// ───────────────────────────────────────────────────────────────────────────

[ApiController]
[Authorize]
[Route("api/sdr/consultas")]
public class SdrConsultasController(SdrConsultaService service, TenantUnitGuard tenantGuard) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int? leadId, CancellationToken ct = default)
    {
        if (tenantGuard.RequireTenant(out var tid) is { } denied) return denied;
        if (tid is null) return BadRequest();
        return Ok(await service.ListAsync(tid.Value, leadId, ct));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] SdrConsultaCreateDto dto, CancellationToken ct = default)
    {
        if (tenantGuard.RequireTenant(out var tid) is { } denied) return denied;
        if (tid is null) return BadRequest();
        try { return Ok(await service.CreateAsync(tid.Value, dto, ct)); }
        catch (ArgumentException ex) { return BadRequest(new ProblemDetails { Title = ex.Message, Status = 400 }); }
        catch (InvalidOperationException ex) { return NotFound(new ProblemDetails { Title = ex.Message, Status = 404 }); }
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct = default)
    {
        if (tenantGuard.RequireTenant(out var tid) is { } denied) return denied;
        if (tid is null) return BadRequest();
        var ok = await service.DeleteAsync(tid.Value, id, ct);
        return ok ? NoContent() : NotFound();
    }
}

// ───────────────────────────────────────────────────────────────────────────
// /api/sdr/tratamentos
// ───────────────────────────────────────────────────────────────────────────

[ApiController]
[Authorize]
[Route("api/sdr/tratamentos")]
public class SdrTratamentosController(SdrTratamentoService service, TenantUnitGuard tenantGuard) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int? leadId, CancellationToken ct = default)
    {
        if (tenantGuard.RequireTenant(out var tid) is { } denied) return denied;
        if (tid is null) return BadRequest();
        return Ok(await service.ListAsync(tid.Value, leadId, ct));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] SdrTratamentoCreateDto dto, CancellationToken ct = default)
    {
        if (tenantGuard.RequireTenant(out var tid) is { } denied) return denied;
        if (tid is null) return BadRequest();
        try { return Ok(await service.CreateAsync(tid.Value, dto, ct)); }
        catch (ArgumentException ex) { return BadRequest(new ProblemDetails { Title = ex.Message, Status = 400 }); }
        catch (InvalidOperationException ex) { return NotFound(new ProblemDetails { Title = ex.Message, Status = 404 }); }
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct = default)
    {
        if (tenantGuard.RequireTenant(out var tid) is { } denied) return denied;
        if (tid is null) return BadRequest();
        var ok = await service.DeleteAsync(tid.Value, id, ct);
        return ok ? NoContent() : NotFound();
    }
}

// ───────────────────────────────────────────────────────────────────────────
// /api/sdr/tarefas
// ───────────────────────────────────────────────────────────────────────────

[ApiController]
[Authorize]
[Route("api/sdr/tarefas")]
public class SdrTarefasController(SdrTarefaService service, TenantUnitGuard tenantGuard) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? status, CancellationToken ct = default)
    {
        if (tenantGuard.RequireTenant(out var tid) is { } denied) return denied;
        if (tid is null) return BadRequest();
        return Ok(await service.ListAsync(tid.Value, status, ct));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] SdrTarefaCreateDto dto, CancellationToken ct = default)
    {
        if (tenantGuard.RequireTenant(out var tid) is { } denied) return denied;
        if (tid is null) return BadRequest();
        try { return Ok(await service.CreateAsync(tid.Value, dto, ct)); }
        catch (ArgumentException ex) { return BadRequest(new ProblemDetails { Title = ex.Message, Status = 400 }); }
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] SdrTarefaUpdateDto dto, CancellationToken ct = default)
    {
        if (tenantGuard.RequireTenant(out var tid) is { } denied) return denied;
        if (tid is null) return BadRequest();
        var t = await service.UpdateAsync(tid.Value, id, dto, ct);
        return t is null ? NotFound() : Ok(t);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct = default)
    {
        if (tenantGuard.RequireTenant(out var tid) is { } denied) return denied;
        if (tid is null) return BadRequest();
        var ok = await service.DeleteAsync(tid.Value, id, ct);
        return ok ? NoContent() : NotFound();
    }
}

// ───────────────────────────────────────────────────────────────────────────
// /api/sdr/agenda
// ───────────────────────────────────────────────────────────────────────────

[ApiController]
[Authorize]
[Route("api/sdr/agenda")]
public class SdrAgendaController(SdrAgendaService service, TenantUnitGuard tenantGuard) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken ct = default)
    {
        if (tenantGuard.RequireTenant(out var tid) is { } denied) return denied;
        if (tid is null) return BadRequest();
        return Ok(await service.ListAsync(tid.Value, from, to, ct));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] SdrAgendaCreateDto dto, CancellationToken ct = default)
    {
        if (tenantGuard.RequireTenant(out var tid) is { } denied) return denied;
        if (tid is null) return BadRequest();
        return Ok(await service.CreateAsync(tid.Value, dto, ct));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] SdrAgendaUpdateDto dto, CancellationToken ct = default)
    {
        if (tenantGuard.RequireTenant(out var tid) is { } denied) return denied;
        if (tid is null) return BadRequest();
        var e = await service.UpdateAsync(tid.Value, id, dto, ct);
        return e is null ? NotFound() : Ok(e);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct = default)
    {
        if (tenantGuard.RequireTenant(out var tid) is { } denied) return denied;
        if (tid is null) return BadRequest();
        var ok = await service.DeleteAsync(tid.Value, id, ct);
        return ok ? NoContent() : NotFound();
    }
}

// ───────────────────────────────────────────────────────────────────────────
// /api/sdr/metas
// ───────────────────────────────────────────────────────────────────────────

[ApiController]
[Authorize]
[Route("api/sdr/metas")]
public class SdrMetasController(SdrMetaService service, TenantUnitGuard tenantGuard) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? mes, CancellationToken ct = default)
    {
        if (tenantGuard.RequireTenant(out var tid) is { } denied) return denied;
        if (tid is null) return BadRequest();
        return Ok(await service.ListAsync(tid.Value, mes, ct));
    }

    [HttpPut]
    public async Task<IActionResult> Upsert([FromBody] SdrMetaUpsertDto dto, CancellationToken ct = default)
    {
        if (tenantGuard.RequireTenant(out var tid) is { } denied) return denied;
        if (tid is null) return BadRequest();
        try { return Ok(await service.UpsertAsync(tid.Value, dto, ct)); }
        catch (ArgumentException ex) { return BadRequest(new ProblemDetails { Title = ex.Message, Status = 400 }); }
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct = default)
    {
        if (tenantGuard.RequireTenant(out var tid) is { } denied) return denied;
        if (tid is null) return BadRequest();
        var ok = await service.DeleteAsync(tid.Value, id, ct);
        return ok ? NoContent() : NotFound();
    }
}

// ───────────────────────────────────────────────────────────────────────────
// /api/sdr/auditoria
// ───────────────────────────────────────────────────────────────────────────

[ApiController]
[Authorize]
[Route("api/sdr/auditoria")]
public class SdrAuditController(SdrAuditLogService audit, TenantUnitGuard tenantGuard) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] string? entityType,
        [FromQuery] int? entityId,
        [FromQuery] string? action,
        [FromQuery] int? userId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        if (tenantGuard.RequireTenant(out var tid) is { } denied) return denied;
        if (tid is null) return BadRequest();
        var rows = await audit.ListAsync(tid.Value, from, to, entityType, entityId, action, userId, page, pageSize, ct);
        var total = await audit.CountAsync(tid.Value, ct);
        Response.Headers["X-Total-Count"] = total.ToString();
        return Ok(rows);
    }
}
