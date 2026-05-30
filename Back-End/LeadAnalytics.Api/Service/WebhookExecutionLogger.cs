using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.Models;

namespace LeadAnalytics.Api.Service;

/// <summary>
/// Grava uma <see cref="WebhookExecution"/> em background, sem segurar a resposta
/// do webhook. A Kommo dá 2s pra responder, então fazemos o log em fire-and-forget
/// usando um <see cref="IServiceScopeFactory"/> próprio (não dá pra usar o scope
/// do request porque ele já foi descartado quando o Task termina).
/// </summary>
public class WebhookExecutionLogger
{
    private const int MaxPayloadBytes = 50 * 1024; // 50 KB
    private const int MaxErrorBytes = 4000;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<WebhookExecutionLogger> _logger;

    public WebhookExecutionLogger(IServiceScopeFactory scopeFactory, ILogger<WebhookExecutionLogger> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <summary>
    /// Dispara o save em background. Se algo der errado, só loga — nunca propaga
    /// pra não derrubar a resposta do webhook.
    /// </summary>
    public void LogInBackground(WebhookExecution exec)
    {
        // Trunca campos que podem crescer demais antes de cair na task.
        exec.RawPayload = Truncate(exec.RawPayload, MaxPayloadBytes, out var truncated);
        exec.PayloadTruncated = truncated;
        exec.ErrorMessage = TruncateSimple(exec.ErrorMessage, MaxErrorBytes);
        exec.ErrorStack = TruncateSimple(exec.ErrorStack, MaxErrorBytes);
        exec.UserAgent = TruncateSimple(exec.UserAgent, 400);

        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                db.WebhookExecutions.Add(exec);
                await db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Falha ao persistir WebhookExecution (slug={Slug})", exec.Slug);
            }
        });
    }

    private static string? Truncate(string? value, int maxBytes, out bool truncated)
    {
        truncated = false;
        if (string.IsNullOrEmpty(value)) return value;
        if (value.Length <= maxBytes) return value;
        truncated = true;
        return value[..maxBytes];
    }

    private static string? TruncateSimple(string? value, int maxChars)
        => string.IsNullOrEmpty(value) || value.Length <= maxChars ? value : value[..maxChars];
}
