using System.Security.Claims;
using LeadAnalytics.Api.DTOs.User;
using LeadAnalytics.Api.Service;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LeadAnalytics.Api.Controllers;

[ApiController]
[Route("users")]
public class UserController(UserService userService) : ControllerBase
{
    private readonly UserService _userService = userService;

    // ─── Perfil do usuário logado ─────────────────────────────────

    [Authorize]
    [HttpGet("me")]
    public async Task<IActionResult> GetMe()
    {
        var id = CurrentUserId();
        if (id is null) return Unauthorized();

        var user = await _userService.GetMeAsync(id.Value);
        if (user is null) return NotFound(new { message = "Usuário não encontrado." });
        return Ok(user);
    }

    [Authorize]
    [HttpPatch("me")]
    public async Task<IActionResult> UpdateMe([FromBody] UpdateMyProfileDto dto)
    {
        var id = CurrentUserId();
        if (id is null) return Unauthorized();
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var (user, error) = await _userService.UpdateMyProfileAsync(id.Value, dto);
        if (user is null) return BadRequest(new { message = error ?? "Falha ao atualizar perfil." });
        return Ok(user);
    }

    [Authorize]
    [HttpPost("me/password")]
    public async Task<IActionResult> ChangeMyPassword([FromBody] ChangeMyPasswordDto dto)
    {
        var id = CurrentUserId();
        if (id is null) return Unauthorized();
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var (ok, error) = await _userService.ChangeMyPasswordAsync(id.Value, dto);
        if (!ok) return BadRequest(new { message = error ?? "Falha ao alterar senha." });
        return NoContent();
    }

    [Authorize]
    [HttpPost("me/photo")]
    [RequestSizeLimit(6 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 6 * 1024 * 1024)]
    public async Task<IActionResult> UploadMyPhoto([FromForm] IFormFile file)
    {
        var id = CurrentUserId();
        if (id is null) return Unauthorized();
        if (file is null) return BadRequest(new { message = "Arquivo obrigatório." });

        var (user, error) = await _userService.SetMyAvatarAsync(id.Value, file);
        if (user is null) return BadRequest(new { message = error ?? "Falha no upload." });
        return Ok(user);
    }

    [Authorize]
    [HttpDelete("me/photo")]
    public async Task<IActionResult> RemoveMyPhoto()
    {
        var id = CurrentUserId();
        if (id is null) return Unauthorized();

        var (ok, error) = await _userService.RemoveMyAvatarAsync(id.Value);
        if (!ok) return BadRequest(new { message = error ?? "Falha ao remover foto." });
        return NoContent();
    }

    // ─── Administração de usuários ────────────────────────────────

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var users = await _userService.GetAllAsync();
        return Ok(users);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        var user = await _userService.GetByIdAsync(id);

        if (user is null)
            return NotFound(new { message = "Usuário não encontrado." });

        return Ok(user);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateUserDto dto)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var created = await _userService.CreateAsync(dto);

        if (created is null)
            return Conflict(new { message = "Já existe um usuário com este e-mail." });

        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateUserDto dto)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var updated = await _userService.UpdateAsync(id, dto);

        if (updated is null)
            return NotFound(new { message = "Usuário não encontrado ou e-mail já em uso." });

        return Ok(updated);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var removed = await _userService.DeleteAsync(id);

        if (!removed)
            return NotFound(new { message = "Usuário não encontrado." });

        return NoContent();
    }

    private int? CurrentUserId()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(raw, out var id) ? id : null;
    }
}
