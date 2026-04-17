using LeadAnalytics.Api.Service;
using Microsoft.AspNetCore.Mvc;

namespace LeadAnalytics.Api.Controllers;

[ApiController]
[Route("daily-relatory")]
public class DailyRelatoryController(DailyRelatoryService dailyRelatoryService) : ControllerBase
{
    private readonly DailyRelatoryService _dailyRelatoryService = dailyRelatoryService;

    [HttpGet("generate")]
    public async Task<IActionResult> Generate([FromQuery] int tenantId, [FromQuery] DateTime date)
    {
        if(!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var relatorio = await _dailyRelatoryService.GenerateDailyRelatory(tenantId, date);
        return Ok(relatorio);
    }
}
