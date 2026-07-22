using LeadAnalytics.Api.Service;
using Microsoft.AspNetCore.Mvc;

namespace LeadAnalytics.Api.Controllers;

/// <summary>
/// Endpoints internos disparados pelo n8n (cron). Substituem os antigos
/// BackgroundServices AlertaPagamentoAtrasadoJob e AlertaPreenchimentoPendenteJob.
/// Protegidos pelo mesmo header X-Admin-Key usado no restante do painel.
/// </summary>
[ApiController]
[Route("internal/alerts")]
public class AlertsController(
    AlertsService alerts,
    InternalApiKeyGuard guard) : ControllerBase
{
    private readonly AlertsService _alerts = alerts;
    private readonly InternalApiKeyGuard _guard = guard;

    private async Task<bool> IsAuthorizedAsync(string? key) => await _guard.IsAuthorizedAsync(key);

    /// <summary>
    /// Marca parcelas vencidas como "atrasado" e devolve a lista do que mudou
    /// para o n8n notificar. Rode 1x/dia via cron do n8n.
    /// </summary>
    [HttpPost("overdue-installments/run")]
    public async Task<IActionResult> RunOverdueInstallments(
        [FromHeader(Name = "X-Admin-Key")] string? adminKey,
        CancellationToken ct)
    {
        if (!await IsAuthorizedAsync(adminKey))
            return Unauthorized(new { message = "Acesso negado" });

        var changed = await _alerts.RunOverdueInstallmentsAsync(ct);
        return Ok(new { count = changed.Count, items = changed });
    }

    /// <summary>
    /// Lista tratamentos aguardando preenchimento da SDR há mais que
    /// <paramref name="hours"/> (default 24). Read-only. Rode de hora em hora.
    /// </summary>
    [HttpGet("pending-fills")]
    public async Task<IActionResult> GetPendingFills(
        [FromHeader(Name = "X-Admin-Key")] string? adminKey,
        [FromQuery] int hours = 24,
        CancellationToken ct = default)
    {
        if (!await IsAuthorizedAsync(adminKey))
            return Unauthorized(new { message = "Acesso negado" });

        var threshold = TimeSpan.FromHours(Math.Clamp(hours, 1, 24 * 30));
        var pending = await _alerts.GetPendingFillsAsync(threshold, ct);
        return Ok(new { count = pending.Count, items = pending });
    }
}
