using System.Text.Json.Serialization;

namespace LeadAnalytics.Api.DTOs.Dashboard;

/// <summary>
/// Análises cruzadas em cima dos campos customizados da Kommo:
/// Sexo × desfecho (consulta/agendamento/fechamento), top valores em
/// Tratamento indicado, Motivo do não agendamento, Profissão, Origem.
/// Tudo em UMA chamada — a página /campos-customizados agrega no cliente.
/// </summary>
public class CustomFieldsCrossAnalysisDto
{
    [JsonPropertyName("total_leads")]
    public int TotalLeads { get; set; }

    /// <summary>Distribuição de Sexo × desfecho (contato/agendou/compareceu/fechou/faltou).</summary>
    [JsonPropertyName("sexo_by_outcome")]
    public List<SexoOutcomeRowDto> SexoByOutcome { get; set; } = new();

    /// <summary>Top valores de Tratamento indicado (multiselect).</summary>
    [JsonPropertyName("tratamento_indicado")]
    public List<ValueCountDto> TratamentoIndicado { get; set; } = new();

    /// <summary>Top valores de Tratamento fechado.</summary>
    [JsonPropertyName("tratamento_fechado")]
    public List<ValueCountDto> TratamentoFechado { get; set; } = new();

    /// <summary>Top motivos de não agendamento.</summary>
    [JsonPropertyName("motivo_nao_agendamento")]
    public List<ValueCountDto> MotivoNaoAgendamento { get; set; } = new();

    /// <summary>Top profissões (text livre — agrupado por exato match).</summary>
    [JsonPropertyName("profissao")]
    public List<ValueCountDto> Profissao { get; set; } = new();

    /// <summary>Top origens (Instagram, Google, Orgânico…).</summary>
    [JsonPropertyName("origem")]
    public List<ValueCountDto> Origem { get; set; } = new();

    /// <summary>Top responsáveis pelo agendamento.</summary>
    [JsonPropertyName("responsavel_agendamento")]
    public List<ValueCountDto> ResponsavelAgendamento { get; set; } = new();

    /// <summary>Top valores de Qualificação do lead (Quente/Morno/Frio).</summary>
    [JsonPropertyName("qualificacao")]
    public List<ValueCountDto> Qualificacao { get; set; } = new();
}

public class SexoOutcomeRowDto
{
    [JsonPropertyName("sexo")] public string Sexo { get; set; } = "";
    [JsonPropertyName("total")] public int Total { get; set; }
    [JsonPropertyName("agendou")] public int Agendou { get; set; }
    [JsonPropertyName("compareceu")] public int Compareceu { get; set; }
    [JsonPropertyName("fechou")] public int Fechou { get; set; }
    [JsonPropertyName("faltou")] public int Faltou { get; set; }
}

public class ValueCountDto
{
    [JsonPropertyName("value")] public string Value { get; set; } = "";
    [JsonPropertyName("count")] public int Count { get; set; }
}
