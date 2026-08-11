namespace LeadAnalytics.Api.DTOs.Spine;

/// <summary>
/// Auditoria de prontuário do CRM web da franquia.
///
/// A unidade de auditoria é o TRATAMENTO, não o atendimento: /atendimentos/acompanhar/{id}
/// abre a mesma ficha para todas as sessões de um tratamento — evolução, questionário de
/// incapacidade e CBDF pertencem ao tratamento. Auditar por atendimento multiplicaria cada
/// achado pelo número de sessões.
/// </summary>
public class AuditoriaDto
{
    public string Unidade { get; set; } = "";
    public string Periodo { get; set; } = "";

    /// <summary>Linhas da listagem varridas (sessões), antes do agrupamento.</summary>
    public int Atendimentos { get; set; }

    /// <summary>Fichas após agrupar por tratamento.</summary>
    public int Total { get; set; }
    public int Avaliacoes { get; set; }
    public int ComAchados { get; set; }
    public int Criticos { get; set; }
    public int Alertas { get; set; }

    public DateTime AtualizadoEm { get; set; }

    public List<AuditoriaProntuarioDto> Prontuarios { get; set; } = [];
    public List<AuditoriaRegraDto> PorRegra { get; set; } = [];
    public List<AuditoriaProfissionalDto> PorProfissional { get; set; } = [];
}

public class AuditoriaRegraDto
{
    public string Regra { get; set; } = "";
    public string Severidade { get; set; } = "";
    public string Titulo { get; set; } = "";
    public int Total { get; set; }
}

public class AuditoriaProfissionalDto
{
    public string Nome { get; set; } = "";
    public int Atendimentos { get; set; }
    public int Criticos { get; set; }
    public int Alertas { get; set; }
}

public class AuditoriaAtendimentoDto
{
    public long Id { get; set; }
    public string Paciente { get; set; } = "";
    public string? Inicio { get; set; }
    public string? Termino { get; set; }

    /// <summary>Null para atendimento em aberto — a listagem devolve lixo de epoch nesse caso.</summary>
    public int? DuracaoMin { get; set; }
    public string Fisioterapeuta { get; set; } = "";
    public string Unidade { get; set; } = "";
    public string Situacao { get; set; } = "";
}

public class AuditoriaEvolucaoDto
{
    public string Data { get; set; } = "";
    public DateOnly? DataIso { get; set; }
    public string Profissional { get; set; } = "";
    public string Protocolo { get; set; } = "";

    /// <summary>"DIA N" do cabeçalho.</summary>
    public int? DiaRotulo { get; set; }

    /// <summary>"PROTOCOLO DO DIA N" citado no corpo — divergir do rótulo é achado.</summary>
    public int? DiaCorpo { get; set; }
    public int? EvaInicial { get; set; }
    public int? EvaFinal { get; set; }
    public string Texto { get; set; } = "";
}

public class AuditoriaQuestionarioDto
{
    /// <summary>Quando a ficha do Roland-Morris foi criada — o campo decisivo da auditoria.</summary>
    public string? CriadoEm { get; set; }
    public DateOnly? CriadoEmIso { get; set; }
    public int? EscoreInicial { get; set; }
    public int? EscoreFinal { get; set; }
}

public class AuditoriaAchadoDto
{
    public string Regra { get; set; } = "";
    public string Severidade { get; set; } = "";
    public string Titulo { get; set; } = "";
    public string Detalhe { get; set; } = "";
}

public class AuditoriaProntuarioDto
{
    public string Chave { get; set; } = "";

    /// <summary>tratamento | avaliacao — avaliação avulsa não tem aba de evolução.</summary>
    public string Tipo { get; set; } = "";
    public long? IdClient { get; set; }
    public long? IdTreatment { get; set; }
    public string NomePaciente { get; set; } = "";
    public int? Idade { get; set; }
    public string Plano { get; set; } = "";
    public string? PrimeiraConsulta { get; set; }
    public DateOnly? PrimeiraIso { get; set; }
    public int? Realizados { get; set; }
    public int? Previstos { get; set; }
    public int? EsteAtendimento { get; set; }
    public string? Prognostico { get; set; }
    public List<string> Cbdf { get; set; } = [];

    public AuditoriaAtendimentoDto Principal { get; set; } = new();
    public List<AuditoriaAtendimentoDto> Atendimentos { get; set; } = [];
    public List<AuditoriaEvolucaoDto> Evolucoes { get; set; } = [];
    public AuditoriaQuestionarioDto? Questionario { get; set; }

    public List<AuditoriaAchadoDto> Achados { get; set; } = [];

    /// <summary>crítico × 10 + alerta × 3 + info × 1.</summary>
    public int Escore { get; set; }
}
