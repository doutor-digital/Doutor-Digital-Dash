using System.Text.Json;
using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.DTOs.Integrations;
using LeadAnalytics.Api.Models;
using LeadAnalytics.Api.Service;
using LeadAnalytics.Api.Service.Ads;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LeadAnalytics.Api.Controllers;

/// <summary>
/// Central de Integrações — conecta contas de Meta Ads / Google Ads por OAuth e expõe o
/// gasto por campanha/dia (gravado no nosso banco). Restrito a analista_ti / super_admin,
/// exceto o callback do OAuth (anônimo, autenticado via state assinado).
/// </summary>
[ApiController]
[Authorize]
[Route("api/integrations/ads")]
public class IntegrationsController(
    AppDbContext db,
    ICurrentUser currentUser,
    ProtectedTokenService tokens,
    AdsSpendSyncService sync,
    AdsCredentialsService credentials,
    IEnumerable<IAdsProvider> providers,
    IConfiguration config,
    ILogger<IntegrationsController> logger) : ControllerBase
{
    private IActionResult? RequireAnalyst() =>
        currentUser.IsAdminLevel
            ? null
            : StatusCode(403, new { message = "Acesso restrito ao analista de TI." });

    private IAdsProvider? Prov(string provider) =>
        providers.FirstOrDefault(p => p.Provider == provider);

    /// <summary>Clínica do usuário (analista tem tenant; super_admin pode passar ?clinicId).</summary>
    private int? ResolveClinicId(int? clinicId) => clinicId ?? currentUser.TenantId;

    // ─── Listagem ────────────────────────────────────────────────────────────

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int? clinicId, CancellationToken ct)
    {
        if (RequireAnalyst() is { } denied) return denied;
        var tenant = ResolveClinicId(clinicId);
        if (tenant is null) return BadRequest(new { message = "clinicId não resolvido." });

        var rows = await db.AdAccounts.AsNoTracking()
            .Where(a => a.ClinicId == tenant.Value)
            .OrderBy(a => a.Provider)
            .ToListAsync(ct);

        var liveByProvider = new Dictionary<string, bool>();
        foreach (var p in providers)
            liveByProvider[p.Provider] = (await credentials.GetAsync(p.Provider, ct)).IsConfigured;

        var items = rows.Select(a => new AdAccountDto
        {
            Id = a.Id,
            Provider = a.Provider,
            ExternalAccountId = a.ExternalAccountId,
            Name = a.Name,
            Status = a.Status,
            LastSyncAt = a.LastSyncAt,
            LastSyncNote = a.LastSyncNote,
            Live = liveByProvider.GetValueOrDefault(a.Provider),
        }).ToList();

        // Providers disponíveis (mesmo sem conta conectada) — pra UI montar os cards.
        var available = providers.Select(p => new { provider = p.Provider, live = liveByProvider.GetValueOrDefault(p.Provider) });
        return Ok(new { items, providers = available });
    }

    // ─── OAuth: início ───────────────────────────────────────────────────────

    [HttpGet("{provider}/connect")]
    public async Task<IActionResult> Connect(
        string provider, [FromQuery] int? clinicId, [FromQuery] int? unitId, CancellationToken ct)
    {
        if (RequireAnalyst() is { } denied) return denied;
        if (Prov(provider) is not { } prov) return BadRequest(new { message = $"Provedor inválido: {provider}" });
        var tenant = ResolveClinicId(clinicId);
        if (tenant is null) return BadRequest(new { message = "clinicId não resolvido." });

        var creds = await credentials.GetAsync(provider, ct);
        var statePayload = JsonSerializer.Serialize(new StateData(tenant.Value, unitId, provider));
        var state = tokens.Protect(statePayload)!;
        var redirectUri = $"{Request.Scheme}://{Request.Host}/api/integrations/ads/{provider}/callback";
        var authUrl = prov.GetAuthUrl(creds, state, redirectUri);

        return Ok(new { auth_url = authUrl, live = creds.IsConfigured });
    }

    // ─── OAuth: callback (anônimo, validado pelo state assinado) ──────────────

    [AllowAnonymous]
    [HttpGet("{provider}/callback")]
    public async Task<IActionResult> Callback(
        string provider,
        [FromQuery] string? code,
        [FromQuery] string? state,
        CancellationToken ct)
    {
        var frontUrl = (config["App:FrontendUrl"] ?? "").TrimEnd('/');
        var fail = (string reason) => Redirect($"{frontUrl}/integracoes/ads?error={Uri.EscapeDataString(reason)}");

        if (Prov(provider) is not { } prov) return fail("provedor inválido");
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(state)) return fail("callback incompleto");

        var raw = tokens.Unprotect(state);
        if (raw is null) return fail("state inválido");
        StateData? st;
        try { st = JsonSerializer.Deserialize<StateData>(raw); }
        catch { return fail("state corrompido"); }
        if (st is null || st.Provider != provider) return fail("state divergente");

        AdsTokenResult token;
        try
        {
            var creds = await credentials.GetAsync(provider, ct);
            var redirectUri = $"{Request.Scheme}://{Request.Host}/api/integrations/ads/{provider}/callback";
            token = await prov.ExchangeCodeAsync(creds, code, redirectUri, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Falha na troca de token do {Provider}", provider);
            return fail("falha ao autorizar");
        }

        // Upsert da conta (uma por clínica+provedor).
        var acct = await db.AdAccounts
            .FirstOrDefaultAsync(a => a.ClinicId == st.ClinicId && a.Provider == provider, ct);
        var now = DateTime.UtcNow;
        if (acct is null)
        {
            acct = new AdAccount { ClinicId = st.ClinicId, Provider = provider, CreatedAt = now };
            db.AdAccounts.Add(acct);
        }
        acct.UnitId = st.UnitId;
        acct.ExternalAccountId = token.ExternalAccountId;
        acct.Name = token.AccountName;
        acct.Status = "connected";
        acct.AccessTokenEnc = tokens.Protect(token.AccessToken);
        acct.RefreshTokenEnc = tokens.Protect(token.RefreshToken);
        acct.TokenExpiresAt = token.ExpiresAt;
        acct.UpdatedAt = now;
        await db.SaveChangesAsync(ct);

        // Sync inicial dos últimos 30 dias pra já haver gasto na tela.
        try
        {
            var to = DateOnly.FromDateTime(now);
            await sync.SyncAsync(acct, to.AddDays(-30), to, ct);
        }
        catch (Exception ex) { logger.LogWarning(ex, "Falha no sync inicial do {Provider}", provider); }

        return Redirect($"{frontUrl}/integracoes/ads?connected={provider}");
    }

    // ─── Sync sob demanda ────────────────────────────────────────────────────

    [HttpPost("{id:int}/sync")]
    public async Task<IActionResult> Sync(int id, CancellationToken ct)
    {
        if (RequireAnalyst() is { } denied) return denied;
        var acct = await db.AdAccounts.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (acct is null) return NotFound(new { message = "Conta não encontrada." });
        if (acct.ClinicId != currentUser.TenantId && !currentUser.IsSuperAdmin)
            return StatusCode(403, new { message = "Conta de outra clínica." });

        var to = DateOnly.FromDateTime(DateTime.UtcNow);
        var count = await sync.SyncAsync(acct, to.AddDays(-30), to, ct);
        return Ok(new { message = "Sincronizado.", rows = count, last_sync_at = acct.LastSyncAt });
    }

    // ─── Desconectar ─────────────────────────────────────────────────────────

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Disconnect(int id, CancellationToken ct)
    {
        if (RequireAnalyst() is { } denied) return denied;
        var acct = await db.AdAccounts.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (acct is null) return NotFound(new { message = "Conta não encontrada." });
        if (acct.ClinicId != currentUser.TenantId && !currentUser.IsSuperAdmin)
            return StatusCode(403, new { message = "Conta de outra clínica." });

        db.AdAccounts.Remove(acct); // o gasto histórico (campaign_daily_spend) cai por cascade.
        await db.SaveChangesAsync(ct);
        return Ok(new { message = "Desconectado." });
    }

    // ─── Gasto agregado (consumido pelo dashboard de Desempenho) ──────────────

    [HttpGet("spend")]
    public async Task<IActionResult> Spend(
        [FromQuery] int? clinicId,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        CancellationToken ct)
    {
        if (RequireAnalyst() is { } denied) return denied;
        var tenant = ResolveClinicId(clinicId);
        if (tenant is null) return BadRequest(new { message = "clinicId não resolvido." });

        var toD = to ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var fromD = from ?? toD.AddDays(-30);

        var items = await db.CampaignDailySpends.AsNoTracking()
            .Where(s => s.ClinicId == tenant.Value && s.Date >= fromD && s.Date <= toD)
            .GroupBy(s => new { s.Provider, s.CampaignId, s.CampaignName, s.Currency })
            .Select(g => new AdsSpendItemDto
            {
                Provider = g.Key.Provider,
                CampaignId = g.Key.CampaignId,
                CampaignName = g.Key.CampaignName,
                Currency = g.Key.Currency,
                Spend = g.Sum(x => x.Spend),
                Impressions = g.Sum(x => x.Impressions),
                Clicks = g.Sum(x => x.Clicks),
                Conversations = g.Sum(x => (long)x.Conversations),
            })
            .OrderByDescending(x => x.Spend)
            .ToListAsync(ct);

        return Ok(new { items, date_from = fromD, date_to = toD });
    }

    // ─── Credenciais do app (configuradas pelo analista) ─────────────────────

    /// <summary>Status das credenciais de cada provedor (sem expor o segredo).</summary>
    [HttpGet("credentials")]
    public async Task<IActionResult> GetCredentials(CancellationToken ct)
    {
        if (RequireAnalyst() is { } denied) return denied;
        var items = new List<object>();
        foreach (var p in providers)
        {
            var st = await credentials.GetStatusAsync(p.Provider, ct);
            items.Add(new
            {
                provider = st.Provider,
                client_id = st.ClientId,
                has_secret = st.HasSecret,
                developer_token = st.DeveloperToken,
                live = st.Live,
                source = st.Source.ToString().ToLowerInvariant(),
            });
        }
        return Ok(new { items });
    }

    /// <summary>Salva (upsert) as credenciais de um provedor. Segredo só troca se vier preenchido.</summary>
    [HttpPut("credentials/{provider}")]
    public async Task<IActionResult> SaveCredentials(
        string provider, [FromBody] AdsCredentialsSaveDto body, CancellationToken ct)
    {
        if (RequireAnalyst() is { } denied) return denied;
        if (Prov(provider) is null) return BadRequest(new { message = $"Provedor inválido: {provider}" });

        await credentials.SaveAsync(
            provider, body.ClientId, body.ClientSecret, body.DeveloperToken, currentUser.Email, ct);

        var st = await credentials.GetStatusAsync(provider, ct);
        return Ok(new { message = "Credenciais salvas.", live = st.Live });
    }

    /// <summary>Conteúdo do state OAuth (assinado/cifrado via DataProtection).</summary>
    private record StateData(int ClinicId, int? UnitId, string Provider);
}
