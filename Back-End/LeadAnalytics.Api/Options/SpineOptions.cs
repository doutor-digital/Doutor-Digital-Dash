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

    /// <summary>
    /// Horários da agenda da franquia (idSchedule) que os cards NÃO contam — registros de
    /// teste lançados na agenda real. VAZIO por padrão: o filtro existe, mas está desligado.
    ///
    /// Por que por id e não por nome ("TESTE"): não há no projeto uma regra segura para
    /// reconhecer paciente de teste pelo nome, e um palpite errado tiraria paciente de
    /// verdade da conta. O id vem de quem conferiu (a recepção ou a auditoria) e só some
    /// o que foi apontado. Para ligar, por variável de ambiente:
    /// <c>Spine__AgendamentosIgnorados__0=3738045</c>, <c>Spine__AgendamentosIgnorados__1=3735665</c>.
    /// O melhor conserto continua sendo a recepção apagar/corrigir o registro na franquia.
    /// </summary>
    public long[] AgendamentosIgnorados { get; set; } = [];
}
