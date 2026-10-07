using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace LeadAnalytics.Api.Service;

/// <summary>
/// Regras de acesso da API num lugar só.
///
/// FECHADO POR PADRÃO
/// ------------------
/// Até 07/10/2026 a regra era o contrário: rota sem <c>[Authorize]</c> ficava aberta.
/// Bastou esquecer o atributo em três controllers (contatos, relatório diário e
/// relatório mensal) para dados de paciente responderem a qualquer um com a URL —
/// conferido em produção, sem token: <c>/daily-relatory/generate</c> devolveu 200.
///
/// Agora toda rota exige login, e a exceção é que precisa ser escrita: quem é público
/// de propósito (webhooks, rotas internas protegidas pelo X-Admin-Key, login) carrega
/// <c>[AllowAnonymous]</c> e um comentário dizendo quem chama. Esquecer o atributo
/// passa a fechar a rota em vez de abri-la — o erro vira 401 no teste, não vazamento.
///
/// O teste <c>RotasPublicasTests</c> lista as rotas anônimas uma a uma: abrir uma rota
/// nova sem querer quebra o teste.
/// </summary>
public static class AuthPolicies
{
    /// <summary>
    /// Só super_admin. Para o que apaga dado de várias unidades de uma vez (remover
    /// unidade, apagar duplicados, administrar usuários).
    /// </summary>
    public const string SuperAdmin = "SuperAdmin";

    public static void Configure(AuthorizationOptions options)
    {
        options.FallbackPolicy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .Build();

        // Mesma leitura de papel do CurrentUser (claim Role, normalizada pelo Roles):
        // aceita as variantes "super-admin"/"superadmin" que já existem no banco.
        options.AddPolicy(SuperAdmin, p => p
            .RequireAuthenticatedUser()
            .RequireAssertion(ctx => Roles.IsSuperAdmin(ctx.User.FindFirst(ClaimTypes.Role)?.Value)));
    }
}
