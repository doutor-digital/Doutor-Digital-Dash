using System.Security.Cryptography;
using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.Models;
using LeadAnalytics.Api.Service;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LeadAnalytics.Api.Controllers;

/// <summary>
/// Liga e desliga o webhook do Asaas por unidade.
///
/// - <c>GET  /api/integrations/asaas/{unitId}</c>  → diz se está ligado e mostra a URL.
/// - <c>POST /api/integrations/asaas/{unitId}</c>  → gera o segredo e devolve UMA vez.
/// - <c>DELETE /api/integrations/asaas/{unitId}</c> → desliga.
///
/// O segredo é mostrado uma única vez, na criação. Depois fica cifrado e nem o
/// painel consegue lê-lo — quem perder gera outro. É a mesma regra de qualquer
/// chave: guardar em lugar de onde dá para ler de novo é o que a torna vazável.
/// </summary>
[ApiController]
[Authorize]
[Route("api/integrations/asaas")]
public class AsaasConfigController(
    AppDbContext db,
    ProtectedTokenService protector,
    ICurrentUser currentUser) : ControllerBase
{
    private IActionResult? RequireAnalyst() =>
        currentUser.IsAdminLevel
            ? null
            : StatusCode(403, new { message = "Acesso restrito ao analista de TI." });

    public record StatusDto(bool Ligado, string UrlWebhook, string[] EventosSugeridos);

    private string UrlDoWebhook(string slug) =>
        $"{Request.Scheme}://{Request.Host}/webhooks/asaas/{slug}";

    /// <summary>Os eventos que interessam ao cartão — os demais só geram ruído.</summary>
    private static readonly string[] Eventos =
    [
        "PAYMENT_CREATED", "PAYMENT_UPDATED", "PAYMENT_CONFIRMED", "PAYMENT_RECEIVED",
        "PAYMENT_RECEIVED_IN_CASH", "PAYMENT_OVERDUE", "PAYMENT_REFUNDED",
        "PAYMENT_DELETED", "PAYMENT_RESTORED", "PAYMENT_CHARGEBACK_REQUESTED",
    ];

    [HttpGet("{unitId:int}")]
    public async Task<IActionResult> Status(int unitId, CancellationToken ct)
    {
        if (RequireAnalyst() is { } denied) return denied;

        var unit = await db.Units.AsNoTracking().FirstOrDefaultAsync(u => u.Id == unitId, ct);
        if (unit is null) return NotFound(new { message = "Unidade não encontrada." });

        var ligado = await db.AppConfigurations.AsNoTracking()
            .AnyAsync(c => c.Key == AsaasWebhookController.ChaveSegredo(unitId), ct);

        return Ok(new StatusDto(ligado, UrlDoWebhook(unit.Slug ?? ""), Eventos));
    }

    /// <summary>Gera um segredo novo. Chamar de novo troca o anterior.</summary>
    [HttpPost("{unitId:int}")]
    public async Task<IActionResult> Ligar(int unitId, CancellationToken ct)
    {
        if (RequireAnalyst() is { } denied) return denied;

        var unit = await db.Units.AsNoTracking().FirstOrDefaultAsync(u => u.Id == unitId, ct);
        if (unit is null) return NotFound(new { message = "Unidade não encontrada." });

        var segredo = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace("+", "").Replace("/", "").Replace("=", "");

        var chave = AsaasWebhookController.ChaveSegredo(unitId);
        var cifrado = protector.Protect(segredo)
            ?? throw new InvalidOperationException("falha ao cifrar o segredo");

        var agora = DateTime.UtcNow;
        var existente = await db.AppConfigurations.FirstOrDefaultAsync(c => c.Key == chave, ct);
        if (existente is null)
            db.AppConfigurations.Add(new AppConfiguration
            {
                Key = chave, Value = cifrado, CreatedAt = agora, UpdatedAt = agora,
            });
        else
        {
            existente.Value = cifrado;
            existente.UpdatedAt = agora;
        }
        await db.SaveChangesAsync(ct);

        return Ok(new
        {
            url = UrlDoWebhook(unit.Slug ?? ""),
            token = segredo,
            eventos = Eventos,
            aviso = "Copie o token agora — ele não é exibido de novo. No Asaas: Integrações → "
                  + "Webhooks → nova configuração, cole a URL e o token no campo de autenticação.",
        });
    }

    [HttpDelete("{unitId:int}")]
    public async Task<IActionResult> Desligar(int unitId, CancellationToken ct)
    {
        if (RequireAnalyst() is { } denied) return denied;

        var chave = AsaasWebhookController.ChaveSegredo(unitId);
        var existente = await db.AppConfigurations.FirstOrDefaultAsync(c => c.Key == chave, ct);
        if (existente is null) return Ok(new { message = "Já estava desligado." });

        db.AppConfigurations.Remove(existente);
        await db.SaveChangesAsync(ct);
        return Ok(new { message = "Webhook do Asaas desligado nesta unidade." });
    }
}
