using System.Text.Json;
using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.Service;
using LeadAnalytics.Api.Service.Asaas;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LeadAnalytics.Api.Controllers;

/// <summary>
/// Webhook do Asaas, isolado por unidade: <c>POST /webhooks/asaas/{slug}</c>.
///
/// AUTENTICAÇÃO
/// ------------
/// O Asaas manda o header <c>asaas-access-token</c> com o valor que você define no
/// painel dele. Guardamos esse segredo cifrado por unidade e comparamos aqui. Sem
/// isso, qualquer pessoa com a URL escreveria valor pago no cartão de um paciente.
/// A comparação é de tempo fixo — comparar string de segredo com <c>==</c> vaza o
/// tamanho do acerto para quem mede o tempo de resposta.
///
/// SEMPRE 200
/// ----------
/// O Asaas repete o evento e SUSPENDE a fila da conta depois de falhas seguidas —
/// uma fila parada significa cobrança paga que não aparece no cartão. Por isso
/// respondemos 200 mesmo quando não dá para processar, dizendo o motivo no corpo,
/// e deixamos o erro no log. A exceção é o segredo errado, que precisa mesmo ser 401.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("webhooks/asaas")]
public class AsaasWebhookController(
    AppDbContext db,
    UnitService unitService,
    AsaasIngestionService ingestion,
    ProtectedTokenService protector,
    WebhookExecutionLogger execLogger,
    ILogger<AsaasWebhookController> logger) : ControllerBase
{
    /// <summary>Chave do segredo por unidade em AppConfiguration.</summary>
    internal static string ChaveSegredo(int unitId) => $"asaas.webhook.{unitId}";

    [HttpPost("{slug}")]
    [ProducesResponseType(200)]
    [ProducesResponseType(401)]
    public async Task<IActionResult> Receber(
        string slug, [FromBody] JsonElement corpo, CancellationToken ct = default)
    {
        var unit = await unitService.ResolveBySlugAsync(slug, ct);
        if (unit is null)
        {
            logger.LogWarning("Webhook Asaas para slug desconhecido: {Slug}", slug);
            return Ok(new { success = false, message = "Unidade não encontrada." });
        }

        var esperado = await SegredoDaUnidadeAsync(unit.Id, ct);
        if (string.IsNullOrWhiteSpace(esperado))
            return Ok(new { success = false, message = "Webhook do Asaas ainda não configurado nesta unidade." });

        Request.Headers.TryGetValue("asaas-access-token", out var recebido);
        if (!SegredoConfere(esperado, recebido.ToString()))
        {
            logger.LogWarning("Webhook Asaas recusado por token inválido | unidade={Unit}", unit.Id);
            return Unauthorized(new { message = "Token do webhook inválido." });
        }

        var bruto = corpo.GetRawText();
        try
        {
            var payload = JsonSerializer.Deserialize<AsaasWebhookPayload>(bruto)
                ?? throw new ArgumentException("Corpo vazio.");

            var r = await ingestion.IngestAsync(payload, unit, ct);

            Registrar(unit, slug, r.Aceito, $"{payload.Event} · cobrança {r.CobrancaId} · {r.Motivo}");

            if (!r.Aceito)
                logger.LogWarning("Evento Asaas não aplicado | {Motivo} | cobrança={Cobranca}", r.Motivo, r.CobrancaId);

            return Ok(new { success = r.Aceito, message = r.Motivo, lead = r.LeadId, campos = r.CamposEscritos });
        }
        catch (Exception ex)
        {
            // 200 de propósito: erro nosso não pode suspender a fila do Asaas.
            logger.LogError(ex, "Falha ao processar webhook Asaas | unidade={Unit} corpo={Corpo}",
                unit.Id, bruto.Length > 800 ? bruto[..800] : bruto);
            Registrar(unit, slug, false, ex.Message);
            return Ok(new { success = false, message = "Evento recebido, mas falhou ao aplicar. Registrado no log." });
        }
    }

    /// <summary>
    /// Trilha de auditoria do evento financeiro: fica gravado o que chegou e o que
    /// fizemos com aquilo, mesmo quando não aplicamos.
    /// </summary>
    private void Registrar(LeadAnalytics.Api.Models.Unit unit, string slug, bool ok, string detalhe) =>
        execLogger.LogInBackground(new LeadAnalytics.Api.Models.WebhookExecution
        {
            Provider = "asaas",
            Slug = slug,
            UnitId = unit.Id,
            TenantId = unit.ClinicId,
            Method = "POST",
            Path = $"/webhooks/asaas/{slug}",
            Ip = HttpContext.Connection.RemoteIpAddress?.ToString(),
            ContentType = Request.ContentType,
            Status = ok ? "success" : "ignored",
            StatusCode = 200,
            Success = ok,
            ErrorMessage = ok ? null : detalhe,
        });

    private async Task<string?> SegredoDaUnidadeAsync(int unitId, CancellationToken ct)
    {
        var row = await db.AppConfigurations.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Key == ChaveSegredo(unitId), ct);
        return row is null ? null : protector.Unprotect(row.Value);
    }

    /// <summary>Comparação de tempo fixo — não sai cedo no primeiro byte diferente.</summary>
    private static bool SegredoConfere(string esperado, string? recebido)
    {
        if (string.IsNullOrEmpty(recebido)) return false;
        var a = System.Text.Encoding.UTF8.GetBytes(esperado);
        var b = System.Text.Encoding.UTF8.GetBytes(recebido);
        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(a, b);
    }
}
