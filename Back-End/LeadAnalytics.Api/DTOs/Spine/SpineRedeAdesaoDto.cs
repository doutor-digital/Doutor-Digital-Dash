namespace LeadAnalytics.Api.DTOs.Spine;

/// <summary>
/// Adesão ao tratamento da rede: sessões realizadas, faltas e desmarcações por unidade.
///
/// SÓ AGREGADOS. Nenhum nome, CPF ou id de paciente sai daqui — a LGPD trata dado de saúde como
/// sensível e este painel é visto por quem compara unidades, não por quem cuida do paciente.
/// Unidade com menos de <see cref="SigiloMinimo"/> pacientes na janela vem com <c>Oculto = true</c> e
/// todos os números nulos: numa clínica pequena, "1 falta" aponta uma pessoa.
/// </summary>
public record SpineRedeAdesaoDto(
    DateOnly De,
    DateOnly Ate,
    int SigiloMinimo,
    IReadOnlyList<SpineRedeAdesaoUnidadeDto> Unidades,
    IReadOnlyList<SpineRedeSemTokenDto> SemToken,
    SpineRedeAdesaoTotaisDto Totais,
    IReadOnlyList<SpineRedeAdesaoSemanaDto> SerieSemanal);

public record SpineRedeAdesaoUnidadeDto(
    int UnitId,
    string Unidade,
    /// <summary>true = poucos pacientes na janela: números omitidos para não identificar ninguém.</summary>
    bool Oculto,
    int? SessoesRealizadas,
    int? Faltas,
    /// <summary>Desmarcadas + remarcadas.</summary>
    int? Desmarcadas,
    /// <summary>realizadas ÷ (realizadas + faltas + desmarcadas), em %.</summary>
    double? TaxaAdesao,
    int? PacientesDistintos,
    double? SessoesPorPaciente,
    int? TratamentosNoPeriodo,
    int? TratamentosEmAndamento,
    string? Erro);

public record SpineRedeAdesaoTotaisDto(
    int Unidades,
    int SessoesRealizadas,
    int Faltas,
    int Desmarcadas,
    double TaxaAdesao,
    int TratamentosNoPeriodo,
    int TratamentosEmAndamento);

/// <summary>Uma semana (segunda-feira como início) somando as unidades da rede.</summary>
public record SpineRedeAdesaoSemanaDto(DateOnly SemanaInicio, int Horarios, int Realizadas);
