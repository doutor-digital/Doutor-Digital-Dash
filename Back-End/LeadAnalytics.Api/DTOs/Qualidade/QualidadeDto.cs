namespace LeadAnalytics.Api.DTOs.Qualidade;

/// <summary>
/// Qualidade do preenchimento dos cartões no período: quanto de cada campo está vazio,
/// quais incoerências existem e como isso se distribui entre os responsáveis.
/// </summary>
public class QualidadeDto
{
    public int Total { get; set; }
    public DateTime De { get; set; }
    public DateTime Ate { get; set; }

    /// <summary>Leads com ao menos uma incoerência. Um lead conta uma vez, não por regra.</summary>
    public int LeadsComIncoerencia { get; set; }

    /// <summary>Percentual de preenchimento a partir do qual o campo é considerado ok.</summary>
    public double Meta { get; set; }

    /// <summary>Campos mapeados que não atingiram a meta — é o número que vira cobrança.</summary>
    public int CamposAbaixoDaMeta { get; set; }

    /// <summary>
    /// Campos sem mapeamento em Configurações Técnicas. É pendência de CONFIGURAÇÃO, não
    /// de preenchimento: sem saber onde o campo mora, medir seria acusar a equipe por
    /// defeito nosso.
    /// </summary>
    public int CamposSemMapeamento { get; set; }

    /// <summary>Ordenado do campo mais vazio para o mais preenchido — o topo é onde doer mais.</summary>
    public List<QualidadeCampoDto> PorCampo { get; set; } = [];

    /// <summary>Só as regras com ocorrência. Regra zerada não vira ruído na tela.</summary>
    public List<QualidadeRegraDto> Regras { get; set; } = [];

    public List<QualidadeResponsavelDto> PorResponsavel { get; set; } = [];
}

public class QualidadeCampoDto
{
    public string Campo { get; set; } = string.Empty;
    public string Rotulo { get; set; } = string.Empty;

    /// <summary>Falso = a unidade não disse onde este campo mora. Não é falha de quem preenche.</summary>
    public bool Mapeado { get; set; }

    /// <summary>
    /// Onde o campo passa a ser exigido, por extenso ("a partir de Agendado"). Sem isso a
    /// tela não explica por que o denominador de um campo é menor que o de outro.
    /// </summary>
    public string Etapa { get; set; } = string.Empty;

    /// <summary>
    /// Quantos leads CHEGARAM na etapa em que o campo é exigido. É o denominador: medir
    /// contra a base inteira faz um campo do agendamento aparecer com 8% porque a maioria
    /// dos leads nunca saiu da qualificação.
    /// </summary>
    public int Universo { get; set; }

    public bool AtingiuMeta { get; set; }
    public int Preenchidos { get; set; }
    public int Vazios { get; set; }

    /// <summary>% preenchido.</summary>
    public double Percentual { get; set; }
}

public class QualidadeRegraDto
{
    public string Id { get; set; } = string.Empty;
    public string Titulo { get; set; } = string.Empty;

    /// <summary>Por que isso importa — o que quebra no número quando acontece.</summary>
    public string Porque { get; set; } = string.Empty;

    public int Quantidade { get; set; }

    /// <summary>
    /// Se existe fonte melhor que a digitação para preencher sozinho. Hoje só a origem
    /// vinda do rastreio; o resto depende de quem atendeu e inventar seria fabricar dado.
    /// </summary>
    public bool Corrigivel { get; set; }

    /// <summary>Amostra (até 500) para o painel abrir a lista.</summary>
    public List<int> LeadIds { get; set; } = [];
}

public class QualidadeResponsavelDto
{
    public string Responsavel { get; set; } = string.Empty;
    public int Total { get; set; }
    public int ComIncoerencia { get; set; }
    public double Percentual { get; set; }
}
