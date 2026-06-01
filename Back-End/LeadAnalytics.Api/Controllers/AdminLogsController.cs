using LeadAnalytics.Api.Service;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LeadAnalytics.Api.Controllers;

/// <summary>
/// Controle de log avançado. Restrito a papéis admin-level (super_admin /
/// analista_ti): sessões de login (IP, geolocalização, minutos ativos), trilha
/// de alteração de entidades e consentimentos de localização.
/// </summary>
[ApiController]
[Route("api/admin")]
[Authorize]
public class AdminLogsController : ControllerBase
{
    private readonly AdminLogService _service;
    private readonly ICurrentUser _currentUser;

    public AdminLogsController(AdminLogService service, ICurrentUser currentUser)
    {
        _service = service;
        _currentUser = currentUser;
    }

    [HttpGet("login-sessions")]
    public async Task<IActionResult> LoginSessions(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int? userId,
        [FromQuery] string? email,
        [FromQuery] bool? active,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        if (!_currentUser.IsAdminLevel) return Forbid();
        return Ok(await _service.QueryLoginSessionsAsync(
            from, to, userId, email, active, page, pageSize, ct));
    }

    [HttpGet("entity-changes")]
    public async Task<IActionResult> EntityChanges(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] string? entityType,
        [FromQuery] string? entityId,
        [FromQuery] int? userId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        if (!_currentUser.IsAdminLevel) return Forbid();
        return Ok(await _service.QueryEntityChangesAsync(
            from, to, entityType, entityId, userId, page, pageSize, ct));
    }

    [HttpGet("location-consents")]
    public async Task<IActionResult> LocationConsents(CancellationToken ct = default)
    {
        if (!_currentUser.IsAdminLevel) return Forbid();
        return Ok(await _service.LocationConsentsAsync(ct));
    }
}
