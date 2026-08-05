namespace LeadAnalytics.Api.DTOs.Saude;

/// <summary>
/// Saúde das fontes que alimentam o dashboard.
///
/// POR QUE ISTO EXISTE
/// -------------------
/// Em 05/08/2026 descobrimos que o sync da Kommo estava parado havia 13 dias — em TODAS as
/// unidades. O dashboard seguiu mostrando os números de 22/07 com a mesma cara de sempre,
/// e a falha só apareceu porque alguém desconfiou de um card.
///
/// Número velho com aparência de novo é pior que tela de erro: leva a decisão errada com
/// confiança. Este endpoint existe para o dashboard conseguir dizer que não está confiável.
/// </summary>
public class SaudeDto
{
    /// <summary>Verdadeiro se alguma fonte está fora do prazo aceitável.</summary>
    public bool TemAlerta { get; set; }

    public List<FonteSaudeDto> Fontes { get; set; } = [];
}

public class FonteSaudeDto
{
    /// <summary>kommo · franquia · ads</summary>
    public string Id { get; set; } = string.Empty;

    public string Nome { get; set; } = string.Empty;

    /// <summary>ok · atrasado · desconectado</summary>
    public string Status { get; set; } = "ok";

    /// <summary>Última vez que esta fonte trouxe dado, em UTC. Nulo = nunca.</summary>
    public DateTime? AtualizadoEm { get; set; }

    /// <summary>Minutos desde a última atualização. Nulo quando nunca atualizou.</summary>
    public int? MinutosAtras { get; set; }

    /// <summary>A partir de quantos minutos esta fonte é considerada atrasada.</summary>
    public int LimiteMinutos { get; set; }

    /// <summary>O que fazer, quando há o que fazer. Vazio quando está tudo certo.</summary>
    public string? Detalhe { get; set; }
}

/// <summary>
/// A agenda da clínica no dia, e o que a Kommo diz do mesmo dia.
///
/// Os dois lado a lado porque em 05/08 a franquia mostrava 4 avaliações e a Kommo 0 — e
/// os dois estavam certos. Só que a divergência apareceu porque alguém abriu os dois
/// sistemas e comparou na mão.
/// </summary>
public class AgendaDoDiaDto
{
    public DateOnly Dia { get; set; }

    /// <summary>Tudo que a clínica tem marcado no dia: avaliações, sessões e retornos.</summary>
    public int TotalNaClinica { get; set; }

    public List<AgendaCategoriaDto> PorCategoria { get; set; } = [];

    /// <summary>Só as avaliações — é o que se compara com o comercial.</summary>
    public int AvaliacoesFranquia { get; set; }

    /// <summary>Leads que ENTRARAM no dia e agendaram. Pergunta diferente, de propósito.</summary>
    public int AgendadosKommo { get; set; }

    /// <summary>Por que os dois números não precisam ser iguais.</summary>
    public string Nota { get; set; } = string.Empty;
}

public class AgendaCategoriaDto
{
    public string Categoria { get; set; } = string.Empty;
    public int Quantidade { get; set; }
    public int Compareceram { get; set; }
    public int Faltaram { get; set; }

    /// <summary>Ainda vai acontecer — é a fila da recepção.</summary>
    public int Pendentes { get; set; }
}
