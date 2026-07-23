using System.Diagnostics;
using System.Text.Json;
using LeadAnalytics.Api.Models;
using LeadAnalytics.Api.Service;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LeadAnalytics.Api.Controllers;

/// <summary>
/// Webhook do agente de I.A. (agente-Dt), isolado POR UNIDADE — mesmo esquema da Kommo.
///
/// URL: <c>POST /webhooks/agent/{slug}</c>. O agente envia JSON com a conversa
/// completa (contato + mensagens) a cada atualização; nós fazemos upsert por
/// (TenantId, conversationId). Sempre responde 2xx rápido pra não acionar retries.
///
/// Contrato (JSON):
/// <code>
/// {
///   "conversationId": "wa-5563999998888",   // obrigatório (id estável da conversa)
///   "agent": "agente-Dt",
///   "channel": "whatsapp",
///   "status": "active",                       // active | closed | handoff (opcional)
///   "contact": { "name": "Maria", "phone": "5563999998888" },
///   "summary": "Quer agendar avaliação",      // opcional
///   "intent": "agendamento", "sentiment": "positivo",  // opcional
///   "handoff": false,                          // true = passou pra humano
///   "startedAt": "2026-06-02T12:00:00Z", "endedAt": null,
///   "tokens": { "in": 1200, "out": 800 },     // opcional
///   "metadata": { },                           // opcional (qualquer json)
///   "messages": [
///     { "role": "user", "content": "Oi", "at": "2026-06-02T12:00:00Z" },
///     { "role": "assistant", "content": "Olá! ...", "at": "2026-06-02T12:00:05Z" }
///   ]
/// }
/// </code>
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("webhooks/agent")]
public class AgentWebhookController(
    UnitService unitService,
    AgentIngestionService ingestionService,
    WebhookExecutionLogger execLogger,
    ILogger<AgentWebhookController> logger) : ControllerBase
{
    private readonly UnitService _unitService = unitService;
    private readonly AgentIngestionService _ingestionService = ingestionService;
    private readonly WebhookExecutionLogger _execLogger = execLogger;
    private readonly ILogger<AgentWebhookController> _logger = logger;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>Recebe do agente de IA (agente-Dt) a conversa de uma unidade e registra mensagens/métricas.</summary>
    /// <remarks>
    /// Endpoint público (sem JWT). URL <c>/webhooks/agent/{slug}</c>. Corpo em JSON com a conversa
    /// (mensagens, identificação do contato). Slug inexistente retorna 200 com <c>success:false</c>.
    /// </remarks>
    /// <param name="slug">Slug público da unidade.</param>
    [HttpPost("{slug}")]
    [Consumes("application/json")]
    public async Task<IActionResult> Receive(string slug, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var startedAt = DateTime.UtcNow;

        var exec = new WebhookExecution
        {
            Provider = "agent",
            Slug = slug,
            ReceivedAt = startedAt,
            Method = Request.Method,
            Path = Request.Path.Value ?? string.Empty,
            Ip = ResolveIp(),
            UserAgent = Request.Headers.UserAgent.ToString(),
            ContentType = Request.ContentType,
            ContentLength = Request.ContentLength,
        };

        _logger.LogInformation("🛬 Webhook agente-Dt chegou | slug={Slug} ct={ContentType}", slug, Request.ContentType);

        object responseBody;

        try
        {
            var unit = await _unitService.ResolveBySlugAsync(slug, ct);

            if (unit is null)
            {
                responseBody = new { success = false, message = "Unidade não encontrada para o slug informado." };
                FinishExec(exec, sw, "ignored", 200, false, "Slug inexistente", null, null, 0, 0, responseBody);
                _execLogger.LogInBackground(exec);
                return Ok(responseBody);
            }

            exec.UnitId = unit.Id;
            exec.TenantId = unit.ClinicId;

            if (!unit.IsActive)
            {
                responseBody = new { success = false, message = "Unidade inativa." };
                FinishExec(exec, sw, "ignored", 200, false, "Unidade inativa", null, null, 0, 0, responseBody);
                _execLogger.LogInBackground(exec);
                return Ok(responseBody);
            }

            using var reader = new StreamReader(Request.Body);
            var raw = await reader.ReadToEndAsync(ct);
            exec.RawPayload = raw;

            if (string.IsNullOrWhiteSpace(raw))
            {
                responseBody = new { success = false, message = "Corpo vazio." };
                FinishExec(exec, sw, "ignored", 200, false, "Corpo vazio", null, null, 0, 0, responseBody);
                _execLogger.LogInBackground(exec);
                return Ok(responseBody);
            }

            AgentWebhookPayload? payload;
            try
            {
                payload = JsonSerializer.Deserialize<AgentWebhookPayload>(raw, JsonOpts);
            }
            catch (JsonException jex)
            {
                _logger.LogWarning(jex, "Webhook agente-Dt ({Slug}) com JSON inválido", slug);
                responseBody = new { success = false, message = "JSON inválido", error = jex.Message };
                FinishExec(exec, sw, "failed", 200, false, jex.Message, jex.ToString(), null, 0, 0, responseBody);
                _execLogger.LogInBackground(exec);
                return Ok(responseBody);
            }

            if (payload is null || string.IsNullOrWhiteSpace(payload.ConversationId ?? payload.Id))
            {
                responseBody = new { success = false, message = "conversationId é obrigatório." };
                FinishExec(exec, sw, "ignored", 200, false, "conversationId ausente", null, null, 0, 0, responseBody);
                _execLogger.LogInBackground(exec);
                return Ok(responseBody);
            }

            var result = await _ingestionService.IngestAsync(payload, unit, ct);

            var summary = JsonSerializer.Serialize(new { result.Created, result.MessageCount });
            responseBody = new
            {
                success = true,
                conversationId = result.ConversationId,
                created = result.Created,
                messages = result.MessageCount,
            };
            FinishExec(exec, sw, "success", 200, true, null, null, summary, payload.Messages.Count, 1, responseBody);
            _execLogger.LogInBackground(exec);

            _logger.LogInformation(
                "✅ Webhook agente-Dt processado | slug={Slug} tenant={Tenant} conv={Id} msgs={Msgs}",
                slug, unit.ClinicId, result.ConversationId, result.MessageCount);

            return Ok(responseBody);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Erro ao processar webhook agente-Dt para slug {Slug}", slug);
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
}
