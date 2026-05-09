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
    KommoAdapter kommoAdapter,
    SdrLeadService sdrLeadService) : ControllerBase
{
    private readonly MetaWebhookService _metaWebhookService = metaWebhookService;
    private readonly ILogger<MetaWebhookController> _logger = logger;
    private readonly LeadService _leadService = leadService;
    private readonly LeadEventService _leadEventService = leadEventService;
    private readonly CloudiaAdapter _cloudiaAdapter = cloudiaAdapter;
    private readonly KommoAdapter _kommoAdapter = kommoAdapter;
    private readonly SdrLeadService _sdrLeadService = sdrLeadService;
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

            // Espelha o lead pra fila de revisão SDR (fluxo CRM novo).
            // Falha silenciosa: se algo der errado aqui, não invalida o lead já gravado.
            try
            {
                await UpsertSdrLeadFromWebhook(webhook);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "⚠️ Falha ao espelhar lead Cloudia em sdr_leads (lead.id={Id}). Lead principal foi salvo.",
                    webhook.Data?.Id);
            }

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
    /// Mapeia o payload do webhook Cloudia em uma row de <c>sdr_leads</c>.
    /// Marca quais campos vieram preenchidos da Cloudia (CloudiaFields) pra que a UI
    /// destaque com o badge emerald de "Auto · Cloudia". Status inicial = "pendente_revisao".
    /// </summary>
    private async Task UpsertSdrLeadFromWebhook(CloudiaWebhookDto webhook)
    {
        var data = webhook.Data ?? webhook.Customer;
        if (data is null || data.Id == 0 || data.ClinicId == 0) return;

        // Detecta tipo (Cadastro vs Resgate) pela origem
        var origem = (data.Origin ?? "").Trim();
        var resgateOrigens = new[]
        {
            "Resgate: Disparo em massa", "Resgate: Disparo de agendamento",
            "Resgate: Ligação", "Resgate: Mensagem", "Resgate:Ligação 3C",
        };
        var isResgate = resgateOrigens.Contains(origem);
        var tipo = isResgate ? "Resgate" : "Cadastro";
        var tipoResgate = isResgate ? origem.Replace("Resgate: ", "").Replace("Resgate:", "").Trim() : null;

        // Stage cloudia "Agendado" → AgendouConsulta=true
        var stageLower = (data.Stage ?? "").ToLowerInvariant();
        var agendou = stageLower.Contains("agendad");

        // Interação: "active" ou "bot" → true
        var convState = (data.ConversationState ?? "").ToLowerInvariant();
        var interacao = convState == "active" || convState == "bot";

        // Set de campos auto-preenchidos pela Cloudia (controla o destaque visual)
        var cloudiaFields = new List<string>();
        if (!string.IsNullOrWhiteSpace(data.Name)) cloudiaFields.Add("nome");
        if (!string.IsNullOrWhiteSpace(data.Phone)) cloudiaFields.Add("telefone");
        if (!string.IsNullOrWhiteSpace(origem)) cloudiaFields.Add("origem");
        cloudiaFields.Add("tipo");
        if (isResgate) cloudiaFields.Add("tipoResgate");
        cloudiaFields.Add("interacao");
        if (!string.IsNullOrWhiteSpace(webhook.AssignedUserName)) cloudiaFields.Add("nomeResponsavel");
        if (!string.IsNullOrWhiteSpace(webhook.AssignedUserEmail)) cloudiaFields.Add("login");
        if (!string.IsNullOrWhiteSpace(data.Observations)) cloudiaFields.Add("observacao");
        if (!string.IsNullOrWhiteSpace(data.Stage)) cloudiaFields.Add("situacao");
        cloudiaFields.Add("dataOrigem");
        if (data.LastUpdatedAt.HasValue) cloudiaFields.Add("dataModificacao");

        await _sdrLeadService.UpsertFromCloudiaAsync(
            tenantId: data.ClinicId,
            externalId: data.Id,
            nome: data.Name ?? "(sem nome)",
            telefone: data.Phone ?? "",
            origem: string.IsNullOrWhiteSpace(origem) ? "Sem origem" : origem,
            tipo: tipo,
            tipoResgate: tipoResgate,
            interacao: interacao,
            agendouConsulta: agendou,
            nomeResponsavel: webhook.AssignedUserName ?? "",
            login: webhook.AssignedUserEmail,
            observacao: data.Observations,
            situacao: data.Stage,
            clinica: null,  // O webhook não traz nome da clínica — só clinic_id (= TenantId).
            dataOrigem: data.CreatedAt ?? DateTime.UtcNow,
            dataModificacao: data.LastUpdatedAt,
            cloudiaFields: cloudiaFields,
            webhookEvent: webhook.Type);
    }

    [HttpPost("kommo")]
    public async Task<IActionResult> ReceiveKommoWebhook([FromBody] KommoWebhookDto webhook)
    {
        try
        {
            _logger.LogInformation("📨 Webhook Kommo recebido: {Id}", webhook.Id);

            var leadEvent = _kommoAdapter.ToLeadEvent(webhook);
            await _leadEventService.ProcessAsync(leadEvent);

            return Ok(new { success = true, message = "Evento Kommo processado" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Erro ao processar webhook do Kommo");
            return Ok(new { success = false, message = "Erro ao processar webhook", error = ex.Message });
        }
    }
}