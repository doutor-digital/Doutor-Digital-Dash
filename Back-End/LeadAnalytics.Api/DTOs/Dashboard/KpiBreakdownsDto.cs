using System.Text.Json.Serialization;

namespace LeadAnalytics.Api.DTOs.Dashboard;

/// <summary>
/// Breakdowns por KPI do dashboard principal. Renderizados inline nos cards
/// (cadastro/resgate/agendados/tratamentos/consultas) sem precisar clicar.
/// </summary>
public class KpiBreakdownsDto
{
    [JsonPropertyName("cadastro")] public CadastroBreakdownDto Cadastro { get; set; } = new();
    [JsonPropertyName("resgate")] public TipoOrigemBreakdownDto Resgate { get; set; } = new();
    [JsonPropertyName("agendados")] public AgendadosBreakdownDto Agendados { get; set; } = new();
    [JsonPropertyName("tratamentos")] public TratamentosBreakdownDto Tratamentos { get; set; } = new();
    [JsonPropertyName("consultas")] public ConsultasBreakdownDto Consultas { get; set; } = new();
}

public class CadastroBreakdownDto
{
    [JsonPropertyName("total")] public int Total { get; set; }
    /// <summary>Por origem: count + motivo de não agendamento mais comum.</summary>
    [JsonPropertyName("origens")] public List<OrigemMotivoDto> Origens { get; set; } = new();
}

public class OrigemMotivoDto
{
    [JsonPropertyName("origem")] public string Origem { get; set; } = "";
    [JsonPropertyName("count")] public int Count { get; set; }
    [JsonPropertyName("top_motivo")] public string? TopMotivo { get; set; }
    [JsonPropertyName("top_motivo_count")] public int TopMotivoCount { get; set; }
}

public class TipoOrigemBreakdownDto
{
    [JsonPropertyName("total")] public int Total { get; set; }
    [JsonPropertyName("tipos")] public List<ValueCountDto> Tipos { get; set; } = new();
    [JsonPropertyName("origens")] public List<ValueCountDto> Origens { get; set; } = new();
}

public class AgendadosBreakdownDto
{
    [JsonPropertyName("total")] public int Total { get; set; }
    [JsonPropertyName("cadastro")] public int Cadastro { get; set; }
    [JsonPropertyName("resgate")] public int Resgate { get; set; }
    [JsonPropertyName("com_pagamento")] public int ComPagamento { get; set; }
    [JsonPropertyName("sem_pagamento")] public int SemPagamento { get; set; }
    [JsonPropertyName("origens")] public List<ValueCountDto> Origens { get; set; } = new();
}

public class TratamentosBreakdownDto
{
    [JsonPropertyName("total")] public int Total { get; set; }
    [JsonPropertyName("origens")] public List<ValueCountDto> Origens { get; set; } = new();
    [JsonPropertyName("fisios")] public List<ValueCountDto> Fisios { get; set; } = new();
    [JsonPropertyName("valor_consulta_total")] public decimal ValorConsultaTotal { get; set; }
    [JsonPropertyName("valor_tratamento_total")] public decimal ValorTratamentoTotal { get; set; }
}

public class ConsultasBreakdownDto
{
    [JsonPropertyName("total")] public int Total { get; set; }
    [JsonPropertyName("cadastro")] public int Cadastro { get; set; }
    [JsonPropertyName("resgate")] public int Resgate { get; set; }
    [JsonPropertyName("valor_total")] public decimal ValorTotal { get; set; }
    /// <summary>Próximos agendamentos (nome + data/hora) — top 8 por data.</summary>
    [JsonPropertyName("agendamentos")] public List<AgendamentoItemDto> Agendamentos { get; set; } = new();
}

public class AgendamentoItemDto
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("when")] public DateTime? When { get; set; }
    [JsonPropertyName("tipo")] public string? Tipo { get; set; }
}
