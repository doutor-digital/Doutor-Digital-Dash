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
