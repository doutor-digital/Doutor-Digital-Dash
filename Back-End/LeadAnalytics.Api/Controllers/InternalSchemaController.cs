using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.Service;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LeadAnalytics.Api.Controllers;

/// <summary>
/// O estado do schema, perguntável. Responde "esta versão subiu com o banco
/// completo?" sem precisar caçar a linha certa no log do contêiner.
/// </summary>
[ApiController]
[Route("internal/schema")]
public class InternalSchemaController(AppDbContext db, InternalApiKeyGuard guard) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(
        [FromHeader(Name = "X-Admin-Key")] string? adminKey,
        CancellationToken ct = default)
    {
        if (!await guard.IsAuthorizedAsync(adminKey)) return Unauthorized();

        // Relê do banco: o estado do startup pode ter sido corrigido à mão depois.
        var pendentesAgora = (await db.Database.GetPendingMigrationsAsync(ct)).ToList();
        var aplicadas = (await db.Database.GetAppliedMigrationsAsync(ct)).ToList();

        return Ok(new
        {
            ok = pendentesAgora.Count == 0 && SchemaHealth.Erro is null,
            noStartup = new
            {
                SchemaHealth.VerificadoEm,
                pendentes = SchemaHealth.Pendentes,
                SchemaHealth.Erro,
            },
            agora = new
            {
                total = aplicadas.Count,
                ultima = aplicadas.LastOrDefault(),
                pendentes = pendentesAgora,
            },
        });
    }
}
