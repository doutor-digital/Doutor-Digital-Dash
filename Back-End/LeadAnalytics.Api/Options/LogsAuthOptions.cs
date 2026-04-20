namespace LeadAnalytics.Api.Options;

public class LogsAuthOptions
{
    public const string SectionName = "LogsAuth";

    /// <summary>Usuário exigido na tela de login do painel de logs.</summary>
    public string Username { get; set; } = "admin";

    /// <summary>Senha exigida na tela de login do painel de logs.</summary>
    public string Password { get; set; } = "change-me";

    /// <summary>Tempo de vida da sessão em minutos (sliding).</summary>
    public int SessionTtlMinutes { get; set; } = 120;
}
