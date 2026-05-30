namespace LeadAnalytics.Api.Service.Stages;

/// <summary>
/// Etapas canônicas do funil (independentes de CRM). A Kommo manda um
/// <c>status_id</c> numérico por pipeline; cada unidade mapeia seus status_ids
/// para estas etapas via <see cref="Models.Unit.KommoStageMapJson"/>.
///
/// Substitui o antigo CloudiaStages — agora a fonte é a Kommo, mas o domínio
/// interno (Consulta/Tratamento) continua reagindo às mesmas etapas lógicas.
/// </summary>
public static class CanonicalStages
{
    public const string EntradaLead          = "ENTRADA_LEAD";
    public const string AgendadoSemPagamento = "AGENDADO_SEM_PAGAMENTO";
    public const string AgendadoComPagamento = "AGENDADO_COM_PAGAMENTO";
    public const string NaoCompareceu        = "NAO_COMPARECEU";
    public const string CompareceuConsulta   = "COMPARECEU_CONSULTA";
    public const string TratamentoFechado    = "TRATAMENTO_FECHADO";
    public const string NaoDeuContinuidade   = "NAO_DEU_CONTINUIDADE";

    /// <summary>Normaliza (trim + UPPER) para comparação estável.</summary>
    public static string Normalize(string? raw) =>
        (raw ?? string.Empty).Trim().ToUpperInvariant();

    private static readonly HashSet<string> _known = new(StringComparer.OrdinalIgnoreCase)
    {
        EntradaLead, AgendadoSemPagamento, AgendadoComPagamento,
        NaoCompareceu, CompareceuConsulta, TratamentoFechado, NaoDeuContinuidade,
    };

    public static bool IsKnown(string? stage) =>
        !string.IsNullOrWhiteSpace(stage) && _known.Contains(stage.Trim());
}
