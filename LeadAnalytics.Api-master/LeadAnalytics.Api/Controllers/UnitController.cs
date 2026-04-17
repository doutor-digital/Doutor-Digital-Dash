using LeadAnalytics.Api.Service;
using Microsoft.AspNetCore.Mvc;

namespace LeadAnalytics.Api.Controllers;

[ApiController]
[Route("units")]
public class UnitController(UnitService unitService) : ControllerBase
{
    private readonly UnitService _unitService = unitService;

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var units = await _unitService.GetAllAsync();
        return Ok(units);
    }

    [HttpGet("{clinicId}")]
    public async Task<IActionResult> GetByClinicId(int clinicId)
    {
        var unit = await _unitService.GetOrCreateAsync(clinicId);

        if (unit is null)
            return NotFound();

        return Ok(unit);
    }

    [HttpPut("{clinicId}")]
    public async Task<IActionResult> UpdateName(int clinicId, [FromBody] string name)
    {
        var unit = await _unitService.GetOrCreateAsync(clinicId);

        if (unit is null)
            return NotFound();

        unit.Name = name;
        return Ok(unit);
    }
    [HttpGet("quantity-leads")]
    public async Task<IActionResult> GetQuantityLeadsUnit(int clinicId)
    {
        var units = await _unitService.GetQuantityLeadsUnit(clinicId);
        if (units is null)
            return NotFound();
        return Ok(units);
    }
}