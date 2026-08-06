namespace LeadAnalytics.Api.DTOs.Jornada;

/// <summary>Um resultado da busca por telefone, nome ou número.</summary>
public class JornadaBuscaItemDto
{
    public int LeadId { get; set; }

    /// <summary>Número do lead na Kommo — é o que a SDR vê na tela do CRM.</summary>
    public string? KommoId { get; set; }

    public string? Nome { get; set; }
    public string? Telefone { get; set; }
    public string? EtapaAtual { get; set; }
    public DateTime CriadoEm { get; set; }
}

/// <summary>
/// A vida de um lead: por onde passou, quanto tempo levou em cada passo, e se a IA está com ele.
/// </summary>
public class JornadaDto
{
    public int LeadId { get; set; }
    public string? KommoId { get; set; }
    public string? Nome { get; set; }
    public string? Telefone { get; set; }
    public string? Origem { get; set; }
    public string? Tipo { get; set; }
    public string? EtapaAtual { get; set; }
    public string? Responsavel { get; set; }
    public DateTime CriadoEm { get; set; }
    public DateTime? DataConsulta { get; set; }
    public string? Qualificacao { get; set; }

    public List<JornadaPassoDto> Passos { get; set; } = [];

    /// <summary>
    /// Linhas de histórico que existem mas não entraram: são as legadas, que guardam a data do
    /// sync e não a da transição. A tela mostra o número para ninguém achar que sumiu passo.
    /// </summary>
    public int PassosDescartados { get; set; }

    /// <summary>
    /// Minutos desde a última coisa registrada. Não é "sem resposta há": não existe registro de
    /// mensagem recebida nesta unidade, e prometer isso seria inventar.
    /// </summary>
    public double MinutosParado { get; set; }

    public double? MinutosAtePrimeiroMovimento { get; set; }
    public double? MinutosAteAgendar { get; set; }

    public JornadaIaDto Ia { get; set; } = new();
}

public class JornadaPassoDto
{
    public string Etapa { get; set; } = string.Empty;

    /// <summary>Rótulo como veio da Kommo — usado para comparar, não para exibir.</summary>
    public string EtapaCrua { get; set; } = string.Empty;

    public DateTime Entrou { get; set; }
    public DateTime? Saiu { get; set; }

    /// <summary>Minutos até o passo seguinte; no último passo, minutos até agora.</summary>
    public double MinutosAte { get; set; }

    public bool Atual { get; set; }

    /// <summary>
    /// A transição dividiu o minuto com muitos outros leads: foi script, não pessoa. Em 24/07
    /// a migração de funil moveu 7 686 leads de uma vez — contar aquilo como atendimento
    /// inventaria produtividade que não houve.
    /// </summary>
    public bool EmLote { get; set; }

    /// <summary>Quantos leads da unidade foram para esta etapa no mesmo minuto.</summary>
    public int NoMesmoMinuto { get; set; }

    /// <summary>Etapa que virou id numérico: status apagado na Kommo, resíduo de funil.</summary>
    public bool Orfa { get; set; }
}

/// <summary>Se a Sofia está com o lead, e o que ela registrou.</summary>
public class JornadaIaDto
{
    /// <summary>Campo "Pausar IA" marcado — a Sofia não responde este lead.</summary>
    public bool Pausada { get; set; }

    /// <summary>
    /// Falso quando ninguém mapeou o campo "Pausar IA" desta unidade. Sem mapeamento a
    /// resposta é "não sei", que é diferente de "não está pausada".
    /// </summary>
    public bool CampoMapeado { get; set; }

    /// <summary>
    /// Não existe conversa gravada para este lead. Em Imperatriz isso vale para todos: o
    /// Salesbot ainda não chama a Sofia, então são 0 conversas e 0 mensagens na unidade
    /// inteira. Sem esta marca, a tela mostraria vazio e pareceria defeito.
    /// </summary>
    public bool SemRegistro { get; set; }

    public int? ConversaId { get; set; }
    public int Mensagens { get; set; }
    public bool PassouParaHumano { get; set; }
    public DateTime? UltimaMensagemEm { get; set; }
    public string? Resumo { get; set; }
}

/// <summary>Quem foi de lead novo a agendado no menor tempo.</summary>
public class JornadaRankingItemDto
{
    public int LeadId { get; set; }
    public string? Nome { get; set; }
    public string? Telefone { get; set; }
    public string? Origem { get; set; }
    public DateTime CriadoEm { get; set; }
    public DateTime AgendouEm { get; set; }
    public double Minutos { get; set; }
}
