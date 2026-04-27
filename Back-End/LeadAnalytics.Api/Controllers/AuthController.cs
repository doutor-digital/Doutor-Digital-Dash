using System.Security.Claims;
using LeadAnalytics.Api.DTOs.Auth;
using LeadAnalytics.Api.Service;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LeadAnalytics.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(AuthService authService, UnitService unitService) : ControllerBase
{
    private readonly AuthService _authService = authService;
    private readonly UnitService _unitService = unitService;

    [HttpPost("login")]
    [ProducesResponseType(typeof(LoginResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Login([FromBody] LoginRequestDto request)
    {
        if (request is null)
            return BadRequest(new { message = "Body de login é obrigatório." });

        if (string.IsNullOrWhiteSpace(request.Email))
            return BadRequest(new { message = "Email é obrigatório para login." });

        if (string.IsNullOrWhiteSpace(request.Password))
            return BadRequest(new { message = "Senha é obrigatória para login." });

        // Garante que a unidade padrão (Araguaína/8020) exista na base.
        await _unitService.GetOrCreateAsync(8020);

        var response = await _authService.LoginAsync(request);
        if (response == null)
            return BadRequest(new { message = "Login inválido." });

        return Ok(response);
    }

    [HttpPost("forgot-password")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequestDto request)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Email))
            return BadRequest(new { message = "Email é obrigatório." });

        await _authService.RequestPasswordResetAsync(request.Email);

        // Resposta sempre genérica para não revelar quais emails existem.
        return Ok(new
        {
            message = "Se houver uma conta para este email, um código de verificação será enviado."
        });
    }

    [HttpPost("verify-reset-code")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> VerifyResetCode([FromBody] VerifyResetCodeRequestDto request)
    {
        if (request is null ||
            string.IsNullOrWhiteSpace(request.Email) ||
            string.IsNullOrWhiteSpace(request.Code))
        {
            return BadRequest(new { message = "Email e código são obrigatórios." });
        }

        var ok = await _authService.VerifyResetCodeAsync(request.Email, request.Code);
        if (!ok)
            return BadRequest(new { message = "Código inválido ou expirado." });

        return Ok(new { message = "Código válido." });
    }

    [HttpPost("reset-password")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequestDto request)
    {
        if (request is null ||
            string.IsNullOrWhiteSpace(request.Email) ||
            string.IsNullOrWhiteSpace(request.Code) ||
            string.IsNullOrWhiteSpace(request.NewPassword))
        {
            return BadRequest(new { message = "Email, código e nova senha são obrigatórios." });
        }

        var (ok, error) = await _authService.ResetPasswordAsync(
            request.Email,
            request.Code,
            request.NewPassword);

        if (!ok)
            return BadRequest(new { message = error ?? "Não foi possível redefinir a senha." });

        return Ok(new { message = "Senha redefinida com sucesso." });
    }

    [Authorize]
    [HttpGet("me")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public IActionResult Me()
    {
        var profile = new
        {
            name = User.FindFirstValue(ClaimTypes.Name),
            email = User.FindFirstValue(ClaimTypes.Email),
            role = User.FindFirstValue(ClaimTypes.Role),
            clinicIds = User.Claims
                .Where(c => c.Type == "clinic_id")
                .Select(c => c.Value)
                .Distinct()
                .ToList(),
            unitIds = User.Claims
                .Where(c => c.Type == "unit_id")
                .Select(c => c.Value)
                .Distinct()
                .ToList()
        };

        return Ok(profile);
    }
}
