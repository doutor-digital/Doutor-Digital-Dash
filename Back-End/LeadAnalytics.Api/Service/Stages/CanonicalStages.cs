using System.Globalization;
using System.Text;

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

    // Etapas do novo funil COMERCIAL/TRATAMENTO (Kommo 2026). Resolvidas por NOME,
    // então unidades no funil antigo (04-10) não são afetadas — só a ITZ, já migrada.
    public const string Qualificacao         = "QUALIFICACAO";          // COMERCIAL · Em Qualificação
    public const string Compareceu           = "COMPARECEU";            // COMERCIAL · Compareceu (veio à consulta)
    public const string Negociacao           = "NEGOCIACAO";            // COMERCIAL · Em Negociação
    public const string Perdido              = "PERDIDO";               // COMERCIAL · Perdido (status 143)
    public const string Alta                 = "ALTA";                  // TRATAMENTO · Alta (status 142)
    public const string TratamentoCancelado  = "TRATAMENTO_CANCELADO";  // TRATAMENTO · Cancelado (status 143)

    /// <summary>Normaliza (trim + UPPER) para comparação estável.</summary>
    public static string Normalize(string? raw) =>
        (raw ?? string.Empty).Trim().ToUpperInvariant();

    private static readonly HashSet<string> _known = new(StringComparer.OrdinalIgnoreCase)
    {
        EntradaLead, AgendadoSemPagamento, AgendadoComPagamento,
        NaoCompareceu, CompareceuConsulta, TratamentoFechado, NaoDeuContinuidade,
        Qualificacao, Compareceu, Negociacao, Perdido, Alta, TratamentoCancelado,
    };

    public static bool IsKnown(string? stage) =>
        !string.IsNullOrWhiteSpace(stage) && _known.Contains(stage.Trim());

    /// <summary>
    /// Resolve um valor "cru" — o valor configurado no <see cref="Models.Unit.KommoStageMapJson"/>
    /// OU o nome da etapa da Kommo — para a etapa canônica. Diferente de <see cref="IsKnown"/>
    /// (que exige o nome canônico exato), é tolerante a como o admin tende a preencher o mapa:
    ///   • prefixo ordinal do nome da etapa na Kommo ("04_AGENDADO_SEM_PAGAMENTO" → AGENDADO_SEM_PAGAMENTO);
    ///   • palavra-chave do nome ("06_FALTOU_CONSULTA" → NAO_COMPARECEU, "01_ENTRADA_LEAD_24H" → ENTRADA_LEAD).
    /// Retorna null quando não reconhece — aí o chamador mantém o status_id cru (comportamento legado).
    /// Nunca sobrescreve um match exato, então não há regressão para mapas já corretos.
    /// </summary>
    public static string? Resolve(string? raw)
    {
        var s = Normalize(raw);
        if (s.Length == 0) return null;

        // 1) Nome canônico exato (igual ao IsKnown).
        if (_known.Contains(s)) return s;

        // 2) Remove prefixo ordinal do nome da Kommo ("04_", "17-", "01 ") e tenta de novo.
        var core = StripLeadingOrdinal(s);
        if (_known.Contains(core)) return core;

        // 3) Funil NOVO COMERCIAL/TRATAMENTO (Kommo 2026). Checado ANTES das heurísticas
        //    legadas porque alguns nomes contêm "TRATAMENTO"/"AGENDADO" e cairiam errado
        //    (ex.: "TRATAMENTO CANCELADO" bateria em "TRATAMENTO"→compareceu). Perdas primeiro.
        if (core.Contains("CANCELAD"))                          return TratamentoCancelado;
        if (core.Contains("PERDIDO"))                           return Perdido;
        if (core.Contains("QUALIFICA"))                         return Qualificacao;
        if (core.Contains("NEGOCIA"))                           return Negociacao;
        if (core.Contains("ALTA"))                              return Alta;
        if (core.Contains("GANHO") || core.Contains("CONCLU"))  return TratamentoFechado;

        // 4) Heurística por palavra-chave (funil ANTIGO 04-10). A ordem importa:
        //    "NAO_FECHOU"/"CONTINUIDADE" precisam ser testados antes de "FECHOU".
        if (core.Contains("AGENDADO"))
            return core.Contains("COM_PAGAMENTO") || core.Contains("COM PAGAMENTO")
                ? AgendadoComPagamento
                : AgendadoSemPagamento;
        // A etapa do funil 2026 chama-se "NÃO COMPARECEU" — com acento e ESPAÇO. As grafias
        // com underline abaixo cobriam só o funil antigo, então o nome real não batia aqui e
        // caía no Contains("COMPARECEU") mais abaixo: 100 faltas ficaram gravadas como
        // comparecimento (Marabá 54, Balsas 38). Comparar sem acento e sem separador cobre
        // todas as grafias de uma vez.
        var semSeparador = SemAcentoNemSeparador(core);
        if (core.Contains("FALTOU")
            || semSeparador.Contains("NAO COMPARECEU")
            || semSeparador.Contains("NO SHOW"))
            return NaoCompareceu;
        if (core.Contains("CONTINUIDADE") || core.Contains("NAO_FECHOU") || core.Contains("NÃO_FECHOU"))
            return NaoDeuContinuidade;
        if (core.Contains("FECHOU") || core.Contains("FECHADO"))
            return TratamentoFechado;
        // "COMPARECEU" isolado = etapa própria do funil novo; testar ANTES de EM_TRATAMENTO.
        if (core.Contains("COMPARECEU"))
            return Compareceu;
        if (core.Contains("EM_TRATAMENTO") || core.Contains("EM TRATAMENTO") || core.Contains("TRATAMENTO"))
            return CompareceuConsulta;
        if (core.Contains("ENTRADA"))
            return EntradaLead;

        return null;
    }

    /// <summary>
    /// Tira acento e troca "_"/"-"/"." por espaço, colapsando espaços repetidos. Serve para
    /// casar o mesmo nome de etapa escrito de jeitos diferentes entre unidades
    /// ("NÃO COMPARECEU", "NAO_COMPARECEU", "nao-compareceu").
    /// </summary>
    private static string SemAcentoNemSeparador(string s)
    {
        var semAcento = new string(s.Normalize(NormalizationForm.FormD)
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            .ToArray());
        var trocado = semAcento.Replace('_', ' ').Replace('-', ' ').Replace('.', ' ');
        return string.Join(' ', trocado.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>Remove um prefixo ordinal ("04_", "17-", "01 ", "3.") do início da string.</summary>
    private static string StripLeadingOrdinal(string s)
    {
        var i = 0;
        while (i < s.Length && char.IsDigit(s[i])) i++;
        if (i == 0) return s; // não começa com dígito — nada a remover
        while (i < s.Length && (s[i] is '_' or '-' or '.' or ' ')) i++;
        return i < s.Length ? s[i..] : s;
    }

    /// <summary>
    /// Converte uma etapa canônica para o código LeadStages.* equivalente, usado pelas
    /// queries do dashboard (CurrentStage). Retorna null para etapas sem equivalência direta
    /// (ex.: EntradaLead — o lead permanece com o status_id cru).
    /// </summary>
    public static string? ToLeadStage(string? canonical) => Normalize(canonical) switch
    {
        AgendadoSemPagamento => LeadStages.AgendadoSemPagamento,
        AgendadoComPagamento => LeadStages.AgendadoComPagamento,
        NaoCompareceu        => LeadStages.Faltou,
        CompareceuConsulta   => LeadStages.EmTratamento,
        TratamentoFechado    => LeadStages.FechouTratamento,
        NaoDeuContinuidade   => LeadStages.NaoFechouTratamento,
        // Funil novo COMERCIAL/TRATAMENTO
        Qualificacao         => LeadStages.Qualificacao,
        Compareceu           => LeadStages.Compareceu,
        Negociacao           => LeadStages.Negociacao,
        Perdido              => LeadStages.Perdido,
        Alta                 => LeadStages.Alta,
        TratamentoCancelado  => LeadStages.TratamentoCancelado,
        _ => null,
    };
}
