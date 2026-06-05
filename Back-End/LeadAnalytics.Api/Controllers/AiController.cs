using LeadAnalytics.Api.Service;
using LeadAnalytics.Api.Service.Ai;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LeadAnalytics.Api.Controllers;

/// <summary>
/// Endpoints da I.A. integrada ao painel (GPT-4o-mini).
///
/// - <c>GET  /api/ai/settings</c>           → diz se a chave está configurada (sem expor o valor).
/// - <c>PUT  /api/ai/settings</c>           → grava a chave (cifrada via DataProtection).
/// - <c>POST /api/ai/settings/test</c>      → ping na OpenAI pra validar a chave.
/// - <c>DELETE /api/ai/settings</c>         → remove a chave.
/// - <c>POST /api/ai/analyze</c>            → análise profunda da unidade no período.
/// - <c>POST /api/ai/transcribe</c>         → multipart áudio → texto (Whisper).
/// </summary>
[ApiController]
[Authorize]
[Route("api/ai")]
public class AiController(
    AiKeyStorage keys,
    OpenAiClient openAi,
    AiAnalyticsService analytics,
    TenantUnitGuard tenantGuard,
    ICurrentUser currentUser,
    ILogger<AiController> logger) : ControllerBase
{
    // ─── SETTINGS ───────────────────────────────────────────────────────────

    public record SettingsDto(bool HasKey);
    public record SetKeyRequest(string ApiKey);
    public record PingResponse(bool Ok, string? Error);

    [HttpGet("settings")]
    public async Task<IActionResult> GetSettings(CancellationToken ct)
    {
        if (currentUser.TenantId is not int tenantId) return Forbid();
        var has = await keys.HasKeyAsync(tenantId, ct);
        return Ok(new SettingsDto(has));
    }

    [HttpPut("settings")]
    public async Task<IActionResult> SetKey([FromBody] SetKeyRequest body, CancellationToken ct)
    {
        if (currentUser.TenantId is not int tenantId) return Forbid();
        if (string.IsNullOrWhiteSpace(body.ApiKey) || body.ApiKey.Length < 20)
            return BadRequest(new { error = "API key inválida (mínimo 20 chars)." });

        await keys.SetAsync(tenantId, body.ApiKey.Trim(), ct);
        logger.LogInformation("[ai] tenant={Tenant} key gravada", tenantId);
        return Ok(new SettingsDto(true));
    }

    [HttpDelete("settings")]
    public async Task<IActionResult> DeleteKey(CancellationToken ct)
    {
        if (currentUser.TenantId is not int tenantId) return Forbid();
        await keys.DeleteAsync(tenantId, ct);
        return Ok(new SettingsDto(false));
    }

    [HttpPost("settings/test")]
    public async Task<IActionResult> Ping(CancellationToken ct)
    {
        if (currentUser.TenantId is not int tenantId) return Forbid();
        var key = await keys.GetAsync(tenantId, ct);
        if (string.IsNullOrWhiteSpace(key))
            return Ok(new PingResponse(false, "Chave não configurada."));
        try
        {
            var ok = await openAi.PingAsync(key, ct);
            return Ok(new PingResponse(ok, ok ? null : "OpenAI rejeitou a chave."));
        }
        catch (Exception ex)
        {
            return Ok(new PingResponse(false, ex.Message));
        }
    }

    // ─── ANALYZE ────────────────────────────────────────────────────────────

    public record AnalyzeRequest(int UnitId, DateTime? DateFrom, DateTime? DateTo);
    public record AnalyzeResponse(string Markdown, int Tokens, double DurationSec);

    [HttpPost("analyze")]
    public async Task<IActionResult> Analyze([FromBody] AnalyzeRequest body, CancellationToken ct)
    {
        var (err, tenantId) = await tenantGuard.ResolveTenantAsync(body.UnitId, ct);
        if (err is not null) return err;
        if (tenantId is null) return Forbid();

        var to = (body.DateTo ?? DateTime.UtcNow).Date.AddDays(1).AddTicks(-1);
        var from = (body.DateFrom ?? to.AddDays(-30)).Date;

        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var md = await analytics.AnalyzeUnitAsync(tenantId.Value, body.UnitId, from, to, ct);
            sw.Stop();
            return Ok(new AnalyzeResponse(md, md.Length / 4, sw.Elapsed.TotalSeconds));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "[ai] erro na OpenAI");
            return StatusCode(502, new { error = ex.Message });
        }
    }

    // ─── TRANSCRIBE ─────────────────────────────────────────────────────────

    [HttpPost("transcribe")]
    [RequestSizeLimit(25 * 1024 * 1024)] // 25MB — limite do Whisper
    public async Task<IActionResult> Transcribe([FromForm] IFormFile? audio, CancellationToken ct)
    {
        if (audio is null || audio.Length == 0) return BadRequest(new { error = "Áudio vazio." });
        if (currentUser.TenantId is not int tenantId) return Forbid();
        var key = await keys.GetAsync(tenantId, ct);
        if (string.IsNullOrWhiteSpace(key))
            return BadRequest(new { error = "Chave da OpenAI não configurada." });

        try
        {
            await using var stream = audio.OpenReadStream();
            var text = await openAi.TranscribeAsync(key, stream, audio.FileName, ct);
            return Ok(new { text });
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "[ai] erro na transcrição");
            return StatusCode(502, new { error = ex.Message });
        }
    }
}
