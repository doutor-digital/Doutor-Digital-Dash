using LeadAnalytics.Api.DTOs.Response;
using LeadAnalytics.Api.Service;
using Microsoft.AspNetCore.Mvc;

namespace LeadAnalytics.Api.Controllers;

[ApiController]
[Route("assignments")]
public class SyncLeadController(SyncN8N syncN8N) : ControllerBase
{
    private readonly SyncN8N _syncN8N = syncN8N;

    [HttpPost("sync")]
    public async Task<IActionResult> SyncLead([FromBody] SyncLeadDto leadData)
    {
        await _syncN8N.SyncLead(leadData);
        return Ok(new { message = "Lead sincronizado com sucesso!" });
    }
}
