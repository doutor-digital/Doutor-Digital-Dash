using LeadAnalytics.Api.Service;
using Microsoft.AspNetCore.Mvc;

namespace LeadAnalytics.Api.Controllers;

/// <summary>
/// Syncs agendados disparados pelo n8n (cron + fan-out por item). Substituem os
/// BackgroundServices KommoSyncPeriodicJob, KommoNightlySyncJob e AdsSpendSyncJob.
///
/// O n8n lista os itens, itera e espaça as chamadas (rate-limit da Kommo = 7 RPS,
/// então ~2s entre unidades). Cada chamada aqui sincroniza UM item e é curta.
/// Protegido pelo header X-Admin-Key.
/// </summary>
[ApiController]
[Route("internal/sync")]
public class InternalSyncController(
    ScheduledSyncService sync,
    InternalApiKeyGuard guard) : ControllerBase
{
    private readonly ScheduledSyncService _sync = sync;
    private readonly InternalApiKeyGuard _guard = guard;

    private async Task<bool> OkAsync(string? key) => await _guard.IsAuthorizedAsync(key);

    // ---- Kommo ----

    /// <summary>Unidades ativas elegíveis para sync Kommo.</summary>
    [HttpGet("kommo/units")]
    public async Task<IActionResult> ListKommoUnits(
        [FromHeader(Name = "X-Admin-Key")] string? adminKey,
        CancellationToken ct)
    {
        if (!await OkAsync(adminKey)) return Unauthorized(new { message = "Acesso negado" });
        var units = await _sync.ListActiveKommoUnitsAsync(ct);
        return Ok(new { count = units.Count, items = units });
    }

    /// <summary>
    /// Sincroniza uma unidade. <c>maxLeads</c>: ~500 incremental (30min),
    /// ~5000 profundo (noturno).
    /// </summary>
    [HttpPost("kommo/units/{unitId:int}")]
    public async Task<IActionResult> SyncKommoUnit(
        int unitId,
        [FromHeader(Name = "X-Admin-Key")] string? adminKey,
        [FromQuery] int maxLeads = 500,
        CancellationToken ct = default)
    {
        if (!await OkAsync(adminKey)) return Unauthorized(new { message = "Acesso negado" });
        var clamped = Math.Clamp(maxLeads, 1, 20000);
        var result = await _sync.SyncKommoUnitAsync(unitId, clamped, ct);
        return Ok(result);
    }

    // ---- Ads ----

    /// <summary>Contas de Ads conectadas.</summary>
    [HttpGet("ads/accounts")]
    public async Task<IActionResult> ListAdAccounts(
        [FromHeader(Name = "X-Admin-Key")] string? adminKey,
        CancellationToken ct)
    {
        if (!await OkAsync(adminKey)) return Unauthorized(new { message = "Acesso negado" });
        var accounts = await _sync.ListConnectedAdAccountsAsync(ct);
        return Ok(new { count = accounts.Count, items = accounts });
    }

    /// <summary>Re-sincroniza o gasto (últimos 30 dias) de uma conta de Ads.</summary>
    [HttpPost("ads/accounts/{accountId:int}")]
    public async Task<IActionResult> SyncAdAccount(
        int accountId,
        [FromHeader(Name = "X-Admin-Key")] string? adminKey,
        CancellationToken ct)
    {
        if (!await OkAsync(adminKey)) return Unauthorized(new { message = "Acesso negado" });
        var result = await _sync.SyncAdAccountAsync(accountId, ct);
        return Ok(result);
    }
}
