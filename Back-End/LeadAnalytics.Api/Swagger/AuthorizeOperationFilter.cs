using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace LeadAnalytics.Api.Swagger;

/// <summary>
/// Anexa o requirement do esquema "Bearer" em cada operação que NÃO é [AllowAnonymous].
/// Usa IDocumentFilter (em vez de IOperationFilter) pra ter acesso ao OpenApiDocument —
/// sem ele o OpenApiSecuritySchemeReference serializa como {} vazio no swagger.json.
/// </summary>
public class AuthorizeOperationFilter : IDocumentFilter
{
    public void Apply(OpenApiDocument document, DocumentFilterContext context)
    {
        if (document.Components?.SecuritySchemes is null
            || !document.Components.SecuritySchemes.ContainsKey("Bearer"))
        {
            return;
        }

        foreach (var apiDescription in context.ApiDescriptions)
        {
            var hasAllowAnonymous =
                apiDescription.CustomAttributes().OfType<AllowAnonymousAttribute>().Any();
            if (hasAllowAnonymous) continue;

            if (!document.Paths.TryGetValue("/" + apiDescription.RelativePath!.TrimStart('/'), out var pathItem))
                continue;

            var method = HttpMethod.Parse(apiDescription.HttpMethod ?? "");
            if (!pathItem.Operations!.TryGetValue(method, out var operation))
                continue;

            operation.Security ??= new List<OpenApiSecurityRequirement>();
            operation.Security.Add(new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference("Bearer", document, null)] = new List<string>(),
            });
        }
    }
}
