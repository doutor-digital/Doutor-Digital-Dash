using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.DTOs.Auth;
using LeadAnalytics.Api.DTOs.Invitations;
using LeadAnalytics.Api.Service;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LeadAnalytics.Api.Controllers;

[ApiController]
[Route("api/invitations")]
public class InvitationsController : ControllerBase
{
    private readonly InvitationService _invitationService;
    private readonly AuthService _authService;
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<InvitationsController> _logger;

    public InvitationsController(
        InvitationService invitationService,
        AuthService authService,
        AppDbContext db,
        ICurrentUser currentUser,
        ILogger<InvitationsController> logger)
    {
        _invitationService = invitationService;
        _authService = authService;
        _db = db;
        _currentUser = currentUser;
        _logger = logger;
    }

    [Authorize]
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] InvitationCreateDto dto, CancellationToken ct)
    {
        if (!_currentUser.UserId.HasValue) return Unauthorized();

        var caller = await _db.Users.FirstOrDefaultAsync(u => u.Id == _currentUser.UserId.Value, ct);
        if (caller == null) return Unauthorized();

        var roleLower = (caller.Role ?? string.Empty).ToLowerInvariant();
        var allowed = roleLower is "super_admin" or "super-admin" or "superadmin" or "sdr" or "manager";
        if (!allowed)
            return Forbid();

        var (result, error) = await _invitationService.CreateAsync(dto, caller, ct);
        if (result == null)
            return BadRequest(new { message = error ?? "Falha ao criar convite." });

        return Ok(result);
    }

    [Authorize]
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int? unitId, CancellationToken ct)
    {
        if (!_currentUser.UserId.HasValue) return Unauthorized();

        var caller = await _db.Users.FirstOrDefaultAsync(u => u.Id == _currentUser.UserId.Value, ct);
        if (caller == null) return Unauthorized();

        var list = await _invitationService.ListPendingAsync(caller, unitId, ct);
        return Ok(list);
    }

    [Authorize]
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Revoke(int id, CancellationToken ct)
    {
        if (!_currentUser.UserId.HasValue) return Unauthorized();

        var caller = await _db.Users.FirstOrDefaultAsync(u => u.Id == _currentUser.UserId.Value, ct);
        if (caller == null) return Unauthorized();

        var ok = await _invitationService.RevokeAsync(id, caller, ct);
        if (!ok) return NotFound();

        return NoContent();
    }

    [HttpGet("{token}/info")]
    [AllowAnonymous]
    public async Task<IActionResult> GetInfo(string token, CancellationToken ct)
    {
        var info = await _invitationService.GetInfoByTokenAsync(token, ct);
        if (info == null)
            return NotFound(new { message = "Convite não encontrado, expirado ou já aceito." });
        return Ok(info);
    }

    [HttpPost("{token}/accept")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(LoginResponseDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Accept(string token, [FromBody] InvitationAcceptDto dto, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto?.IdToken))
            return BadRequest(new { message = "idToken é obrigatório." });

        var (response, error) = await _authService.AcceptInvitationWithGoogleAsync(token, dto.IdToken, ct);
        if (response == null)
            return BadRequest(new { message = error ?? "Falha ao aceitar convite." });

        return Ok(response);
    }
}
