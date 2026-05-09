using LeadAnalytics.Api.Service;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LeadAnalytics.Api.Controllers;

[ApiController]
[Route("api/audit-logs")]
[Authorize]
public class AuditLogsController : ControllerBase
{
    private readonly AuditLogService _auditLogService;
    private readonly ICurrentUser _currentUser;

    public AuditLogsController(AuditLogService auditLogService, ICurrentUser currentUser)
    {
        _auditLogService = auditLogService;
        _currentUser = currentUser;
    }

    [HttpGet]
    public async Task<IActionResult> Query(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int? userId,
        [FromQuery] string? email,
        [FromQuery] string? path,
        [FromQuery] string? ip,
        [FromQuery] int? statusCode,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        if (!_currentUser.IsSuperAdmin)
            return Forbid();

        var data = await _auditLogService.QueryAsync(
            from, to, userId, email, path, ip, statusCode, page, pageSize, ct);

        return Ok(data);
    }
}
