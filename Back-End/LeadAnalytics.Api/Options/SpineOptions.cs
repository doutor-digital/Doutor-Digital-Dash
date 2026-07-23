namespace LeadAnalytics.Api.Options;

/// <summary>
/// Configuração da API Spine (sistema clínico do Doutor Hérnia).
///
/// O token é emitido pelo suporte do Doutor Hérnia por unidade (escopo idCompany)
/// e NÃO fica no appsettings versionado: é lido de AppConfiguration
/// (chave "spine:token:{unitId}") ou da env var SPINE_TOKEN como fallback.
/// </summary>
public class SpineOptions
{
    public const string SectionName = "Spine";

    /// <summary>Base de produção. Homologação: https://app-api-hom.doutorhernia.com.br</summary>
    public string BaseUrl { get; set; } = "https://app-api-prod.doutorhernia.com.br";

    /// <summary>Timeout do cliente. O servidor deles corta em 30s (guia §12).</summary>
    public int TimeoutSeconds { get; set; } = 25;

    /// <summary>Segundos de cache das respostas agregadas. Agenda muda devagar.</summary>
    public int CacheSeconds { get; set; } = 300;
}
