using System.Diagnostics;
using System.Text;
using System.Text.Json;
using LeadAnalytics.Api.Adapters;
using LeadAnalytics.Api.Models;
using LeadAnalytics.Api.Service;
using LeadAnalytics.Api.Service.Ai;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LeadAnalytics.Api.Controllers;

/// <summary>
/// Webhook dedicado da Kommo, isolado POR UNIDADE.
///
/// Cada unidade tem um slug único e uma URL própria:
/// <c>POST /webhooks/kommo/{slug}</c>. O usuário cola essa URL em
/// Configurações → Integrações → Web hooks da conta Kommo daquela unidade.
/// Assim os dados já entram separados por tenant (Unit.ClinicId).
///
/// A Kommo envia <c>application/x-www-form-urlencoded</c> com notação de colchetes
/// (ex.: <c>leads[add][0][id]=123</c>) e espera resposta 2xx em até 2 segundos —
/// senão reenvia (até 5x) e pode desativar o webhook. Por isso respondemos sempre
/// 2xx, rápido, e nunca lançamos exceção pra fora.
///
/// Cada chamada vira uma linha em <see cref="WebhookExecution"/> (fire-and-forget)
/// pra alimentar o painel /webhooks-monitor — útil pra debug.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("webhooks/kommo")]
public class KommoWebhookController(
    UnitService unitService,
    KommoAdapter kommoAdapter,
    KommoIngestionService ingestionService,
    KommoStagesResolver stagesResolver,
    WebhookExecutionLogger execLogger,
    ILogger<KommoWebhookController> logger) : ControllerBase
{
    private readonly UnitService _unitService = unitService;
    private readonly KommoAdapter _kommoAdapter = kommoAdapter;
    private readonly KommoIngestionService _ingestionService = ingestionService;
    private readonly KommoStagesResolver _stagesResolver = stagesResolver;
    private readonly WebhookExecutionLogger _execLogger = execLogger;
    private readonly ILogger<KommoWebhookController> _logger = logger;

    [HttpPost("{slug}")]
    [Consumes("application/x-www-form-urlencoded")]
    public async Task<IActionResult> Receive(string slug, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var startedAt = DateTime.UtcNow;

        var exec = new WebhookExecution
        {
            Provider = "kommo",
            Slug = slug,
            ReceivedAt = startedAt,
            Method = Request.Method,
            Path = Request.Path.Value ?? string.Empty,
            Ip = ResolveIp(),
            UserAgent = Request.Headers.UserAgent.ToString(),
            ContentType = Request.ContentType,
            ContentLength = Request.ContentLength,
        };

        _logger.LogInformation(
            "🛬 Webhook Kommo chegou | slug={Slug} ct={ContentType}",
            slug, Request.ContentType);

        object responseBody;

        try
        {
            var unit = await _unitService.ResolveBySlugAsync(slug, ct);

            if (unit is null)
            {
                _logger.LogWarning("Webhook Kommo para slug inexistente: {Slug}", slug);
                responseBody = new { success = false, message = "Unidade não encontrada para o slug informado." };
                FinishExec(exec, sw, "ignored", 200, false, "Slug inexistente", null, null, 0, 0, responseBody);
                _execLogger.LogInBackground(exec);
                return Ok(responseBody);
            }

            exec.UnitId = unit.Id;
            exec.TenantId = unit.ClinicId;
            exec.KommoSubdomain = unit.KommoSubdomain;

            if (!unit.IsActive)
            {
                _logger.LogInformation("Webhook Kommo para unidade inativa: {Slug}", slug);
                responseBody = new { success = false, message = "Unidade inativa." };
                FinishExec(exec, sw, "ignored", 200, false, "Unidade inativa", null, null, 0, 0, responseBody);
                _execLogger.LogInBackground(exec);
                return Ok(responseBody);
            }

            if (!Request.HasFormContentType)
            {
                _logger.LogWarning(
                    "Webhook Kommo ({Slug}) sem corpo de formulário (Content-Type={ContentType})",
                    slug, Request.ContentType);
                responseBody = new { success = false, message = "Formato inesperado: esperado x-www-form-urlencoded" };
                FinishExec(exec, sw, "ignored", 200, false, "Content-Type inesperado", null, null, 0, 0, responseBody);
                _execLogger.LogInBackground(exec);
                return Ok(responseBody);
            }

            var form = await Request.ReadFormAsync(ct);

            // Captura o payload bruto pra debug.
            exec.RawPayload = BuildRawPayload(form);
            exec.FormKeyCount = form.Keys.Count;

            var topKeys = string.Join(",", form.Keys
                .Select(k => k.Contains('[') ? k[..k.IndexOf('[')] : k)
                .Distinct()
                .Take(15));
            exec.FormKeys = topKeys;

            _logger.LogInformation(
                "📋 Webhook Kommo form recebido | slug={Slug} totalKeys={Total} topKeys=[{Keys}]",
                slug, form.Keys.Count, topKeys);

            var payload = KommoFormParser.Parse(form);
            exec.KommoAccountId = payload.Account?.Id;
            if (string.IsNullOrEmpty(exec.KommoSubdomain))
                exec.KommoSubdomain = payload.Account?.Subdomain;

            var events = _kommoAdapter.ToLeadEvents(payload);

            var summaryDict = events
                .GroupBy(e => $"{e.EntityType}:{e.Action}")
                .ToDictionary(g => g.Key, g => g.Count());
            var summaryJson = JsonSerializer.Serialize(summaryDict);
            var summaryStr = string.Join(",", summaryDict.Select(kv => $"{kv.Key}={kv.Value}"));

            _logger.LogInformation(
                "🔀 Webhook Kommo eventos parseados | slug={Slug} total={Count} breakdown=[{Summary}]",
                slug, events.Count, summaryStr);

            // Auto-resolve por nome (status_id → canônica) reusando o cache do KommoStagesResolver.
            // Mesmo papel do override que o sync passa: resolve agendados quando a unidade não
            // tem KommoStageMapJson configurado. O mapa explícito da unidade ainda vence (mesclado
            // dentro do IngestAsync). Falha em silêncio: sem token/Kommo offline → mapa vazio.
            var stageMapOverride = await _stagesResolver.GetCanonicalStageMapAsync(unit.Id, ct);

            // recordStageHistory: true — o webhook ao vivo é a única fonte (junto do backfill
            // de eventos) que sabe o instante real da transição de etapa. O sync REST passa false.
            var persisted = await _ingestionService.IngestAsync(events, unit, ct, stageMapOverride, recordStageHistory: true);

            _logger.LogInformation(
                "✅ Webhook Kommo processado | unidade={Slug} (tenant={Tenant}) account={Account} eventos={Count} leadsPersistidos={Persisted}",
                slug, unit.ClinicId, payload.Account?.Subdomain ?? payload.Account?.Id, events.Count, persisted);

            responseBody = new { success = true, eventsReceived = events.Count, leadsPersisted = persisted };
            FinishExec(exec, sw, "success", 200, true, null, null, summaryJson, events.Count, persisted, responseBody);
            _execLogger.LogInBackground(exec);

            return Ok(responseBody);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Erro ao processar webhook Kommo para slug {Slug}", slug);
            responseBody = new { success = false, message = "Erro ao processar webhook", error = ex.Message };
            FinishExec(exec, sw, "failed", 200, false, ex.Message, ex.ToString(), null, 0, 0, responseBody);
            _execLogger.LogInBackground(exec);
            return Ok(responseBody);
        }
    }

    private static void FinishExec(
        WebhookExecution exec, Stopwatch sw, string status, int statusCode, bool success,
        string? errorMessage, string? errorStack, string? eventsSummary,
        int eventsParsed, int leadsPersisted, object responseBody)
    {
        sw.Stop();
        exec.DurationMs = (int)sw.ElapsedMilliseconds;
        exec.Status = status;
        exec.StatusCode = statusCode;
        exec.Success = success;
        exec.ErrorMessage = errorMessage;
        exec.ErrorStack = errorStack;
        exec.EventsSummary = eventsSummary;
        exec.EventsParsed = eventsParsed;
        exec.LeadsPersisted = leadsPersisted;
        exec.ResponseBody = JsonSerializer.Serialize(responseBody);
    }

    private string? ResolveIp()
    {
        var fwd = Request.Headers["X-Forwarded-For"].ToString();
        if (!string.IsNullOrWhiteSpace(fwd))
        {
            var first = fwd.Split(',').FirstOrDefault()?.Trim();
            if (!string.IsNullOrEmpty(first)) return first;
        }
        return HttpContext.Connection.RemoteIpAddress?.ToString();
    }

    /// <summary>Reconstroi o body urlencoded original (truncado depois).</summary>
    private static string BuildRawPayload(IFormCollection form)
    {
        var sb = new StringBuilder();
        var first = true;
        foreach (var (k, v) in form)
        {
            foreach (var item in v)
            {
                if (!first) sb.Append('&');
                first = false;
                sb.Append(Uri.EscapeDataString(k));
                sb.Append('=');
                sb.Append(Uri.EscapeDataString(item ?? string.Empty));
            }
        }
        return sb.ToString();
    }
}
