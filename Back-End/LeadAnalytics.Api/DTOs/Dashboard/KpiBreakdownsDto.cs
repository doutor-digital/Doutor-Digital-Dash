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
    /// <summary>
    /// Leads que JÁ tinham entrada em agendado* ANTES do período e só fizeram a transição
    /// 04↔05 dentro dele (reclassificação de pagamento, não agendamento novo). Não somam
    /// em <see cref="Total"/> — chip informativo no card pra SDR entender a diferença.
    /// </summary>
    [JsonPropertyName("reclassificacoes")] public int Reclassificacoes { get; set; }
    [JsonPropertyName("origens")] public List<ValueCountDto> Origens { get; set; } = new();
    /// <summary>Quebra pelo custom field "Tipo de agendamento" (consulta/retorno/avaliação...).</summary>
    [JsonPropertyName("tipos_agendamento")] public List<ValueCountDto> TiposAgendamento { get; set; } = new();
}

public class TratamentosBreakdownDto
{
    [JsonPropertyName("total")] public int Total { get; set; }
    [JsonPropertyName("origens")] public List<ValueCountDto> Origens { get; set; } = new();
    [JsonPropertyName("fisios")] public List<ValueCountDto> Fisios { get; set; } = new();
    [JsonPropertyName("valor_consulta_total")] public decimal ValorConsultaTotal { get; set; }
    [JsonPropertyName("valor_tratamento_total")] public decimal ValorTratamentoTotal { get; set; }
    /// <summary>Quebra pelo custom field "Tipo de tratamento" (fisioterapia/pilates/...).</summary>
    [JsonPropertyName("tipos_tratamento")] public List<ValueCountDto> TiposTratamento { get; set; } = new();
}

public class ConsultasBreakdownDto
{
    [JsonPropertyName("total")] public int Total { get; set; }
    [JsonPropertyName("cadastro")] public int Cadastro { get; set; }
    [JsonPropertyName("resgate")] public int Resgate { get; set; }
    [JsonPropertyName("valor_total")] public decimal ValorTotal { get; set; }
    /// <summary>Consultas cuja DATA DA CONSULTA (AppointmentScheduledAt) cai no período
    /// selecionado — "consultas do dia". Diferente de <see cref="Total"/>, que conta
    /// quando a SDR MARCOU a consulta (produtividade).</summary>
    [JsonPropertyName("do_dia")] public int DoDia { get; set; }
    /// <summary>Desfecho das consultas do dia.</summary>
    [JsonPropertyName("compareceu")] public int Compareceu { get; set; }
    [JsonPropertyName("faltou")] public int Faltou { get; set; }
    [JsonPropertyName("aguardando")] public int Aguardando { get; set; }
    /// <summary>Próximos agendamentos (nome + data/hora) — top 8 por data.</summary>
    [JsonPropertyName("agendamentos")] public List<AgendamentoItemDto> Agendamentos { get; set; } = new();
}

public class AgendamentoItemDto
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("when")] public DateTime? When { get; set; }
    [JsonPropertyName("tipo")] public string? Tipo { get; set; }
}

/// <summary>Item da faixa "consultas de hoje" — inclui hora e desfecho pra UI.</summary>
public class ConsultaDiaItemDto
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("when")] public DateTime? When { get; set; }
    [JsonPropertyName("tipo")] public string? Tipo { get; set; }
    /// <summary>"compareceu" | "faltou" | "aguardando".</summary>
    [JsonPropertyName("outcome")] public string Outcome { get; set; } = "aguardando";
    [JsonPropertyName("phone")] public string? Phone { get; set; }
}
