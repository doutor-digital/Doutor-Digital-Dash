using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace LeadAnalytics.Api.Service;

/// <summary>
/// Exige o header <c>X-Logs-Token</c> (ou Authorization Bearer) válido
/// no painel de logs — independente da autenticação JWT do app.
/// </summary>
public class LogsTokenAuthorizeAttribute : Attribute, IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var svc = context.HttpContext.RequestServices.GetRequiredService<LogsAuthService>();

        var token = context.HttpContext.Request.Headers["X-Logs-Token"].ToString();
        if (string.IsNullOrWhiteSpace(token))
        {
            var auth = context.HttpContext.Request.Headers.Authorization.ToString();
            if (!string.IsNullOrEmpty(auth) && auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                token = auth["Bearer ".Length..].Trim();
        }

        if (!svc.Validate(token))
        {
            context.Result = new UnauthorizedObjectResult(new
            {
                message = "Sessão do painel de logs inválida ou expirada.",
            });
        }
    }
}
