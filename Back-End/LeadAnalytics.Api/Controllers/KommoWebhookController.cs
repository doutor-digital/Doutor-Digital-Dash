using LeadAnalytics.Api.Adapters;
using LeadAnalytics.Api.Service;
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
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("webhooks/kommo")]
public class KommoWebhookController(
    UnitService unitService,
    KommoAdapter kommoAdapter,
    KommoIngestionService ingestionService,
    ILogger<KommoWebhookController> logger) : ControllerBase
{
    private readonly UnitService _unitService = unitService;
    private readonly KommoAdapter _kommoAdapter = kommoAdapter;
    private readonly KommoIngestionService _ingestionService = ingestionService;
    private readonly ILogger<KommoWebhookController> _logger = logger;

    [HttpPost("{slug}")]
    [Consumes("application/x-www-form-urlencoded")]
    public async Task<IActionResult> Receive(string slug, CancellationToken ct)
    {
        // Log de entrada visível em qualquer log level — quero ver no Railway
        // que o webhook chegou mesmo se algo der errado depois.
        _logger.LogInformation(
            "🛬 Webhook Kommo chegou | slug={Slug} ct={ContentType} formKeys={Has}",
            slug, Request.ContentType, Request.HasFormContentType);

        try
        {
            var unit = await _unitService.ResolveBySlugAsync(slug, ct);

            if (unit is null)
            {
                _logger.LogWarning("Webhook Kommo para slug inexistente: {Slug}", slug);
                return Ok(new { success = false, message = "Unidade não encontrada para o slug informado." });
            }

            if (!unit.IsActive)
            {
                _logger.LogInformation("Webhook Kommo para unidade inativa: {Slug}", slug);
                return Ok(new { success = false, message = "Unidade inativa." });
            }

            if (!Request.HasFormContentType)
            {
                _logger.LogWarning(
                    "Webhook Kommo ({Slug}) sem corpo de formulário (Content-Type={ContentType})",
                    slug, Request.ContentType);
                return Ok(new { success = false, message = "Formato inesperado: esperado x-www-form-urlencoded" });
            }

            var form = await Request.ReadFormAsync(ct);

            // Mostra as chaves principais que vieram (sem valores, pra não vazar dados).
            // Ajuda muito a debugar quando "lead chegou mas nada salvou".
            var topKeys = string.Join(",", form.Keys
                .Select(k => k.Contains('[') ? k[..k.IndexOf('[')] : k)
                .Distinct()
                .Take(15));

            _logger.LogInformation(
                "📋 Webhook Kommo form recebido | slug={Slug} totalKeys={Total} topKeys=[{Keys}]",
                slug, form.Keys.Count, topKeys);

            var payload = KommoFormParser.Parse(form);
            var events = _kommoAdapter.ToLeadEvents(payload);

            // Quebra eventos por entityType + action pra ver o que tá entrando.
            var summary = string.Join(",", events
                .GroupBy(e => $"{e.EntityType}:{e.Action}")
                .Select(g => $"{g.Key}={g.Count()}"));

            _logger.LogInformation(
                "🔀 Webhook Kommo eventos parseados | slug={Slug} total={Count} breakdown=[{Summary}]",
                slug, events.Count, summary);

            var persisted = await _ingestionService.IngestAsync(events, unit, ct);

            _logger.LogInformation(
                "✅ Webhook Kommo processado | unidade={Slug} (tenant={Tenant}) account={Account} eventos={Count} leadsPersistidos={Persisted}",
                slug, unit.ClinicId, payload.Account?.Subdomain ?? payload.Account?.Id, events.Count, persisted);

            return Ok(new { success = true, eventsReceived = events.Count, leadsPersisted = persisted });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Erro ao processar webhook Kommo para slug {Slug}", slug);
            return Ok(new { success = false, message = "Erro ao processar webhook", error = ex.Message });
        }
    }
}
