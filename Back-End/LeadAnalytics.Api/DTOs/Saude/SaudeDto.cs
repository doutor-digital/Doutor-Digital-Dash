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

/// <summary>
/// Problemas na base e na configuração que inflam ou zeram números em silêncio.
/// Todos os itens aqui já aconteceram nesta operação sem dar erro em tela.
/// </summary>
public class HigieneDto
{
    public int TotalLeads { get; set; }
    public List<HigieneAchadoDto> Achados { get; set; } = [];

    /// <summary>Estado de cada campo mapeado em Configurações Técnicas.</summary>
    public List<HigieneCampoDto> Configuracao { get; set; } = [];

    public int ConfiguracaoComProblema { get; set; }
}

public class HigieneAchadoDto
{
    public string Id { get; set; } = string.Empty;
    public string Titulo { get; set; } = string.Empty;
    public int Quantidade { get; set; }
    public double Percentual { get; set; }

    /// <summary>O que este problema faz com os números.</summary>
    public string Impacto { get; set; } = string.Empty;

    /// <summary>O que dá para fazer a respeito.</summary>
    public string Acao { get; set; } = string.Empty;
}

public class HigieneCampoDto
{
    public string Campo { get; set; } = string.Empty;

    /// <summary>ok · sem_mapeamento · nao_encontrado</summary>
    public string Situacao { get; set; } = "ok";

    public string? Detalhe { get; set; }
}

/// <summary>
/// O que precisa de alguém agora. Métrica diz o que aconteceu; fila diz o que fazer.
/// </summary>
public class FilasDto
{
    public int TotalPendente { get; set; }

    /// <summary>Só as filas com item. Lista de zeros ensina a equipe a ignorar o bloco.</summary>
    public List<FilaDto> Filas { get; set; } = [];
}

public class FilaDto
{
    public string Id { get; set; } = string.Empty;
    public string Titulo { get; set; } = string.Empty;

    /// <summary>Por que isso não pode esperar.</summary>
    public string Porque { get; set; } = string.Empty;

    /// <summary>alta · media</summary>
    public string Urgencia { get; set; } = "media";

    public int Quantidade { get; set; }
    public List<FilaItemDto> Itens { get; set; } = [];
}

public class FilaItemDto
{
    public int? LeadId { get; set; }
    public string? Nome { get; set; }
    public string? Telefone { get; set; }
    public string? Detalhe { get; set; }
    public DateTime? Quando { get; set; }
}

/// <summary>
/// O que aconteceu no CRM, na ordem em que aconteceu — a prova bruta por trás dos números.
/// Fica no fim da página: no topo competiria com as filas, e perderia.
/// </summary>
public class AtividadeDto
{
    public List<AtividadeLinhaDto> Linhas { get; set; } = [];

    /// <summary>Quantas coisas aconteceram na última hora — dá o tamanho do movimento.</summary>
    public int NaUltimaHora { get; set; }

    /// <summary>Só os leads novos da última hora.</summary>
    public int EntraramNaUltimaHora { get; set; }

    /// <summary>
    /// Instante da linha mais recente. Nulo quando não houve nada em 24 h — a tela precisa
    /// dizer isso, porque log vazio é indistinguível de log quebrado.
    /// </summary>
    public DateTime? MaisRecente { get; set; }
}

public class AtividadeLinhaDto
{
    public int? LeadId { get; set; }
    public DateTime Quando { get; set; }

    /// <summary>lead · etapa · agenda · campo</summary>
    public string Tipo { get; set; } = string.Empty;

    /// <summary>ok · atencao · ruim · neutro — define a cor da linha.</summary>
    public string Tom { get; set; } = "neutro";

    public string Texto { get; set; } = string.Empty;
}

/// <summary>
/// Conferência dos números: cada item é uma afirmação que tem de ser verdade.
/// Teste unitário prova que a regra está certa; isto prova que o número da tela bate.
/// </summary>
public class ConferenciaDto
{
    public DateTime De { get; set; }
    public DateTime Ate { get; set; }
    public List<ChecagemDto> Checagens { get; set; } = [];

    /// <summary>Quantas não fecharam. Zero é o único número aceitável aqui.</summary>
    public int Falharam { get; set; }
}

public class ChecagemDto
{
    public string Id { get; set; } = string.Empty;

    /// <summary>A afirmação, escrita como afirmação — não como pergunta.</summary>
    public string Titulo { get; set; } = string.Empty;

    /// <summary>Por que isso importa, em uma frase que a SDR entenda.</summary>
    public string Explica { get; set; } = string.Empty;

    public int ValorA { get; set; }
    public string RotuloA { get; set; } = string.Empty;
    public int ValorB { get; set; }
    public string RotuloB { get; set; } = string.Empty;

    /// <summary>O que fazer, ou o tamanho exato da diferença.</summary>
    public string Detalhe { get; set; } = string.Empty;

    public bool Passou { get; set; }
}
