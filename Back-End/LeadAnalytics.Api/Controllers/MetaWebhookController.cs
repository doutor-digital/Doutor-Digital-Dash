using LeadAnalytics.Api.Adapters;
using LeadAnalytics.Api.DTOs;
using LeadAnalytics.Api.DTOs.Cloudia;
using LeadAnalytics.Api.DTOs.Kommo;
using LeadAnalytics.Api.DTOs.Meta;
using LeadAnalytics.Api.Service;
using Microsoft.AspNetCore.Mvc;

namespace LeadAnalytics.Api.Controllers;

[ApiController]
[Route("api/webhooks")]
public class MetaWebhookController(
    MetaWebhookService metaWebhookService,
    ILogger<MetaWebhookController> logger,
    LeadService leadService,
    LeadEventService leadEventService,
    CloudiaAdapter cloudiaAdapter,
    KommoAdapter kommoAdapter) : ControllerBase
{
    private readonly MetaWebhookService _metaWebhookService = metaWebhookService;
    private readonly ILogger<MetaWebhookController> _logger = logger;
    private readonly LeadService _leadService = leadService;
    private readonly LeadEventService _leadEventService = leadEventService;
    private readonly CloudiaAdapter _cloudiaAdapter = cloudiaAdapter;
    private readonly KommoAdapter _kommoAdapter = kommoAdapter;
    /// <summary>
    /// Endpoint de verificação do webhook da Meta
    /// </summary>
    [HttpGet("meta")]
    public IActionResult VerifyWebhook(
        [FromQuery(Name = "hub.mode")] string? mode,
        [FromQuery(Name = "hub.verify_token")] string? token,
        [FromQuery(Name = "hub.challenge")] string? challenge)
    {
        const string VERIFY_TOKEN = "seu_token"; // TODO: Mover para appsettings.json

        if (mode == "subscribe" && token == VERIFY_TOKEN)
        {
            _logger.LogInformation("✅ Webhook verificado com sucesso");
            return Ok(challenge);
        }

        _logger.LogWarning("❌ Falha na verificação do webhook");
        return Unauthorized();
    }

    /// <summary>
    /// Recebe eventos da Meta via n8n
    /// </summary>
    [HttpPost("meta")]
    public async Task<IActionResult> ReceiveMetaWebhook([FromBody] MetaWebhookDto webhook)
    {
        try
        {
            // Guard logging that could evaluate potentially expensive properties when logging is disabled
            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation("📨 Webhook Meta recebido: {Object}", webhook.Object);
            }

            var result = await _metaWebhookService.ProcessWebhookAsync(webhook);

            return Ok(new
            {
                success = true,
                message = "Webhook processado com sucesso",
                eventsProcessed = result.EventsProcessed,
                originEventsCreated = result.OriginEventsCreated
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Erro ao processar webhook da Meta");

            // Retorna 200 mesmo com erro para não reenviar webhook
            return Ok(new
            {
                success = false,
                message = "Erro ao processar webhook",
                error = ex.Message
            });
        }
    }

    /// <summary>
    /// Endpoint alternativo caso n8n envie formato customizado
    /// </summary>
    [HttpPost("meta/n8n")]
    public async Task<IActionResult> ReceiveN8nWebhook([FromBody] N8nWebhookDto webhook)
    {
        try
        {
            // Guard logging that could evaluate potentially expensive properties when logging is disabled
            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation("📨 Webhook n8n recebido para telefone: {Phone}", webhook.Phone);
            }

            var result = await _metaWebhookService.ProcessN8nWebhookAsync(webhook);

            return Ok(new
            {
                success = true,
                message = "Evento processado com sucesso",
                originEventId = result.OriginEventId
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Erro ao processar webhook do n8n");

            return Ok(new
            {
                success = false,
                message = "Erro ao processar webhook",
                error = ex.Message
            });
        }
    }

    // <summary>
    /// Recebe webhooks da Cloudia
    /// Eventos: CUSTOMER_CREATED, CUSTOMER_UPDATED, CUSTOMER_TAGS_UPDATED, USER_ASSIGNED_TO_CUSTOMER
    /// </summary>
    [HttpPost("cloudia")]
    public async Task<IActionResult> ReceiveCloudiaWebhook([FromBody] CloudiaWebhookDto webhook)
    {
        try
        {
            _logger.LogInformation("📨 Webhook Cloudia recebido: {Type}", webhook.Type);

            var leadEvent = _cloudiaAdapter.ToLeadEvent(webhook);
            await _leadEventService.ProcessAsync(leadEvent);

            var result = await _leadService.SaveLeadAsync(webhook);

            var message = result.Result switch
            {
                ProcessResult.Created => "Lead criado com sucesso",
                ProcessResult.Updated => "Lead atualizado com sucesso",
                ProcessResult.Ignored => "Evento ignorado",
                _ => "Processado"
            };

            return Ok(new
            {
                success = true,
                message,
                result = result.Result.ToString()
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Erro ao processar webhook da Cloudia");

            return Ok(new
            {
                success = false,
                message = "Erro ao processar webhook",
                error = ex.Message
            });
        }
    }

    /// <summary>
    /// Recebe webhooks do Kommo CRM.
    ///
    /// O Kommo envia <c>application/x-www-form-urlencoded</c> com notação de colchetes
    /// aninhada (ex.: <c>leads[add][0][id]=123</c>), NÃO JSON — por isso lemos
    /// <see cref="Microsoft.AspNetCore.Http.HttpRequest.Form"/> e parseamos manualmente.
    ///
    /// O serviço do Kommo espera resposta 2xx em até 2 segundos, senão reenvia o webhook
    /// (até 5 tentativas) e pode desativá-lo após muitas respostas inválidas. Por isso
    /// processamos rápido e sempre devolvemos 2xx.
    /// </summary>
    [HttpPost("kommo")]
    [Consumes("application/x-www-form-urlencoded")]
    public async Task<IActionResult> ReceiveKommoWebhook()
    {
        try
        {
            if (!Request.HasFormContentType)
            {
                _logger.LogWarning("⚠️ Webhook Kommo recebido sem corpo de formulário (Content-Type={ContentType})", Request.ContentType);
                return Ok(new { success = false, message = "Formato inesperado: esperado x-www-form-urlencoded" });
            }

            var form = await Request.ReadFormAsync();
            var payload = KommoFormParser.Parse(form);
            var events = _kommoAdapter.ToLeadEvents(payload);

            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation(
                    "📨 Webhook Kommo recebido | account={Account} eventos={Count}",
                    payload.Account?.Subdomain ?? payload.Account?.Id, events.Count);
            }

            foreach (var ev in events)
                await _leadEventService.ProcessAsync(ev);

            return Ok(new { success = true, message = "Webhook Kommo processado", eventsProcessed = events.Count });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Erro ao processar webhook do Kommo");
            // 2xx para evitar reenvio/desativação do webhook no Kommo.
            return Ok(new { success = false, message = "Erro ao processar webhook", error = ex.Message });
        }
    }
}