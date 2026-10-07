namespace LeadAnalytics.Api.Service;

/// <summary>
/// Registro dos HttpClients que falam com a Kommo — o tipado <see cref="KommoApiClient"/> e o
/// nomeado <c>"kommo"</c> (usado pelo <see cref="CloudiaKommoPatchService"/>).
///
/// <para><b>Sem cookies, de propósito.</b> Toda resposta da Kommo traz
/// <c>Set-Cookie: session_id=…; domain=.kommo.com</c>. O handler padrão do HttpClient guarda esse
/// cookie e o manda para TODAS as contas <c>*.kommo.com</c>. Quando a chamada seguinte vai para
/// OUTRA conta, a Kommo dá preferência à sessão do cookie sobre o Bearer e responde
/// <c>401 "Nome de usuário ou senha inválidos"</c> — com o token certo. O handler é
/// compartilhado pelo app inteiro (sync, cruzamento, telas do painel), então qualquer chamada
/// envenena a próxima. Medido em 07/10/2026: com o mesmo jarro de cookies, na ordem do cron,
/// as contas alternam 200 / 401 / 200 / 401; sem cookie, as 22 respondem 200.</para>
///
/// <para>O token é Bearer de longa duração, então cookie nenhum é necessário aqui.</para>
/// </summary>
public static class KommoHttp
{
    /// <summary>Nome do HttpClient nomeado usado por quem monta a URL na mão.</summary>
    public const string ClienteNomeado = "kommo";

    /// <summary>Handler da Kommo: nunca guarda nem envia cookie.</summary>
    public static SocketsHttpHandler CriarHandler() => new() { UseCookies = false };

    public static IServiceCollection AddKommoHttpClients(this IServiceCollection services)
    {
        services.AddHttpClient<KommoApiClient>(c => c.Timeout = TimeSpan.FromSeconds(30))
            .ConfigurePrimaryHttpMessageHandler(CriarHandler);

        services.AddHttpClient(ClienteNomeado)
            .ConfigurePrimaryHttpMessageHandler(CriarHandler);

        return services;
    }
}
