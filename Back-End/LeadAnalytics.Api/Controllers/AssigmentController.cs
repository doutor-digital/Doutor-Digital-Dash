using LeadAnalytics.Api.Service;
using Microsoft.AspNetCore.Mvc;

namespace LeadAnalytics.Api.Controllers;

[ApiController]
[Route("assignments")]
public class AssignmentController(
    AttendantService attendantService,
    ILogger<AssignmentController> logger) : ControllerBase
{
    private readonly AttendantService _attendantService = attendantService;
    private readonly ILogger<AssignmentController> _logger = logger;

    [HttpGet("attendants")]
    public async Task<IActionResult> GetAllAttendants()
    {
        var attendants = await _attendantService.GetAllAsync();
        return Ok(attendants);
    }

    [HttpGet("lead/{externalLeadId}")]
    public async Task<IActionResult> GetByLead(int externalLeadId, [FromQuery] int clinicId)
    {
        var assignments = await _attendantService.GetAssignmentsByLeadAsync(externalLeadId, clinicId);

        if (assignments is null || assignments.Count == 0)
            return NotFound("Nenhuma atribuição encontrada para esse lead.");

        return Ok(assignments);
    }

    [HttpGet("ranking")]
    public async Task<IActionResult> GetRanking([FromQuery] int? clinicId = null)
    {
        var ranking = await _attendantService.GetRankingAsync(clinicId);
        return Ok(ranking);
    }
}