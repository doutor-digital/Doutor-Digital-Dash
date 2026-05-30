using LeadAnalytics.Api.DTOs.Units;
using LeadAnalytics.Api.Service;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LeadAnalytics.Api.Controllers;

/// <summary>
/// CRUD das unidades (clínicas/filiais). Cada unidade é um tenant isolado e tem uma
/// URL de webhook própria da Kommo (<c>/webhooks/kommo/{slug}</c>), retornada em
/// <see cref="UnitDto.WebhookUrl"/> para o usuário colar nas configurações da Kommo.
/// </summary>
[ApiController]
[Authorize]
[Route("units")]
public class UnitController(UnitService unitService, IConfiguration configuration) : ControllerBase
{
    private readonly UnitService _unitService = unitService;
    private readonly IConfiguration _configuration = configuration;

    /// <summary>Base pública usada para montar a URL do webhook (config "Webhook:PublicBaseUrl" ou host atual).</summary>
    private string BaseUrl()
    {
        var configured = _configuration["Webhook:PublicBaseUrl"];
        return string.IsNullOrWhiteSpace(configured)
            ? $"{Request.Scheme}://{Request.Host}"
            : configured.TrimEnd('/');
    }

    /// <summary>Lista todas as unidades (com URL do webhook e contagem de leads).</summary>
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
        => Ok(await _unitService.ListDtosAsync(BaseUrl(), ct));

    /// <summary>Detalhe de uma unidade pelo Id.</summary>
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id, CancellationToken ct)
    {
        var unit = await _unitService.GetDtoByIdAsync(id, BaseUrl(), ct);
        return unit is null ? NotFound() : Ok(unit);
    }

    /// <summary>Cria uma nova unidade (botão "+"). Retorna a unidade com a URL do webhook gerada.</summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateUnitDto dto, CancellationToken ct)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        try
        {
            var created = await _unitService.CreateAsync(dto, BaseUrl(), ct);
            return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    /// <summary>Atualiza dados/cadastro/configurações de uma unidade.</summary>
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateUnitDto dto, CancellationToken ct)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var updated = await _unitService.UpdateAsync(id, dto, BaseUrl(), ct);
        return updated is null ? NotFound() : Ok(updated);
    }

    /// <summary>Remove uma unidade (apenas se não houver leads vinculados).</summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        try
        {
            var removed = await _unitService.DeleteAsync(id, ct);
            return removed ? NoContent() : NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    [HttpGet("quantity-leads")]
    public async Task<IActionResult> GetQuantityLeadsUnit(int clinicId)
    {
        var units = await _unitService.GetQuantityLeadsUnit(clinicId);
        if (units is null)
            return NotFound();
        return Ok(units);
    }

    // ─── Compatibilidade com o comportamento anterior (por ClinicId) ──────

    [HttpGet("by-clinic/{clinicId:int}")]
    public async Task<IActionResult> GetByClinicId(int clinicId)
    {
        var unit = await _unitService.GetOrCreateAsync(clinicId);
        return unit is null ? NotFound() : Ok(unit);
    }

    [HttpPut("by-clinic/{clinicId:int}")]
    public async Task<IActionResult> RenameByClinic(int clinicId, [FromBody] string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return BadRequest(new { message = "Nome inválido" });

        var unit = await _unitService.RenameAsync(clinicId, name);
        return Ok(unit);
    }
}
