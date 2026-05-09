namespace LeadAnalytics.Api.Service.Stages;

/// <summary>
/// Constantes das etapas Cloudia que disparam ações específicas no nosso sistema.
/// O dispatcher tenta normalizar (lowercase, trim, prefixos) antes de comparar.
/// </summary>
public static class CloudiaStages
{
    // Etapas com handler específico
    public const string EntradaLead             = "01_ENTRADA_LEAD";
    public const string LeadSemResposta         = "02_LEAD_SEM_RESPOSTA";
    public const string LeadQuenteQualificado   = "03_LEAD_QUENTE_QUALIFICADO";
    public const string AgendadoSemPagamento    = "04_AGENDADO_SEM_PAGAMENTO";
    public const string AgendadoComPagamento    = "05_AGENDADO_COM_PAGAMENTO";
    public const string NaoCompareceu           = "06_NAO_COMPARECEU";
    public const string CompareceuConsulta      = "07_COMPARECEU_CONSULTA";
    public const string TratamentoFechado       = "09_TRATAMENTO_FECHADO";
    public const string Cancelamento            = "12_CANCELAMENTO";
    public const string AltaSatisfeito          = "13_ALTA_SATISFEITO";
    public const string AltaInsatisfeito        = "14_ALTA_INSATISFEITO";
    public const string NaoPerturbar            = "15_NAO_PERTURBAR";
    public const string Encaminhado             = "16_ENCAMINHADO";
    public const string NaoDeuContinuidade      = "17_NAO_DEU_CONTINUIDADE";

    /// <summary>Normaliza o stage (uppercase, trim) pra match consistente.</summary>
    public static string Normalize(string? raw) =>
        (raw ?? string.Empty).Trim().ToUpperInvariant();
}
