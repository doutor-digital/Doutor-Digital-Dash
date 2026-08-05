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
