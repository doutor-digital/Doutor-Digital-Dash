namespace LeadAnalytics.Api.DTOs.Response;

/// <summary>
/// Fechamento do dia por unidade — o relatório que a equipe manda no fim do expediente.
///
/// Conta LEADS, não atribuições. A versão anterior partia de LeadAssignments e o total
/// vinha errado por dois lados ao mesmo tempo: lead sem responsável não aparecia (e
/// nenhum lead novo tem responsável hoje), e lead reatribuído contava duas vezes.
/// </summary>
public class DailyRelatoryDto
{
    public string Unidade { get; set; } = string.Empty;
    public int UnidadeId { get; set; }

    public int TotalLeads { get; set; }
    public int Agendamentos { get; set; }

    /// <summary>Agendados que já garantiram a consulta no PIX antecipado.</summary>
    public int AgendadosComAntecipado { get; set; }

    /// <summary>Agendados sem antecipado — é aqui que mora o no-show.</summary>
    public int AgendadosSemAntecipado { get; set; }

    /// <summary>Entraram e não marcaram. O complemento de Agendamentos.</summary>
    public int NaoAgendaram { get; set; }

    /// <summary>Fecharam tratamento.</summary>
    public int ComPagamento { get; set; }

    public int Resgastes { get; set; }

    /// <summary>% do total que agendou. Pré-calculado para a tela não repetir a conta.</summary>
    public double TaxaAgendamento { get; set; }

    /// <summary>De onde vieram os leads do dia. Do maior para o menor.</summary>
    public List<RelatorioContagemDto> PorOrigem { get; set; } = [];

    /// <summary>Termômetro dos leads do dia (Quente/Morno/Frio).</summary>
    public List<RelatorioContagemDto> PorQualificacao { get; set; } = [];

    /// <summary>Motivo de quem não agendou — do campo da Kommo, não de heurística em texto livre.</summary>
    public List<RelatorioContagemDto> MotivosNaoAgendamento { get; set; } = [];

    /// <summary>
    /// O que ficou faltando preencher nos cartões do dia. É o que torna o relatório
    /// conferível: sem isso, número baixo parece resultado ruim quando é campo vazio.
    /// </summary>
    public List<RelatorioPendenciaDto> Pendencias { get; set; } = [];

    public List<string> Atendentes { get; set; } = [];

    /// <summary>Mantido para quem já consome o texto corrido.</summary>
    public string Observacoes { get; set; } = string.Empty;
}

public class RelatorioContagemDto
{
    public string Rotulo { get; set; } = string.Empty;
    public int Quantidade { get; set; }
    public double Percentual { get; set; }
}

/// <summary>Um campo que ficou vazio em N cartões do dia.</summary>
public class RelatorioPendenciaDto
{
    public string Campo { get; set; } = string.Empty;

    /// <summary>O que deixa de funcionar enquanto estiver vazio.</summary>
    public string Impacto { get; set; } = string.Empty;

    public int Quantidade { get; set; }
}
