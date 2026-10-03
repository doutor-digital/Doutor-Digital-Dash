namespace LeadAnalytics.Api.DTOs.Spine;

/// <summary>
/// Adesão ao tratamento da rede: sessões realizadas, faltas, desmarcações e horários sem baixa, por unidade.
///
/// SÓ AGREGADOS. Nenhum nome, CPF ou id de paciente sai daqui — a LGPD trata dado de saúde como sensível e
/// este painel é visto por quem compara unidades, não por quem cuida do paciente.
///  - Unidade com menos de <see cref="SigiloMinimo"/> pacientes que contaram na conta vem <c>Oculto = true</c>
///    e com todos os números nulos: numa clínica pequena, "1 falta" aponta uma pessoa.
///  - A janela é FIXA (30, 60 ou 90 dias terminando ontem): janelas livres permitiriam subtrair duas respostas
///    vizinhas e isolar um dia de uma unidade, que às vezes é um paciente só.
/// </summary>
public record SpineRedeAdesaoDto(
    DateOnly De,
    DateOnly Ate,
    int Dias,
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
    /// <summary>Desmarcadas pelo paciente. Remarcada NÃO entra: ela gera outro horário, que conta por si.</summary>
    int? Desmarcadas,
    /// <summary>Horário que já passou e continua "agendado/confirmado": a recepção não deu baixa.</summary>
    int? SemBaixa,
    /// <summary>realizadas ÷ (realizadas + faltas + desmarcadas + sem baixa), em %. null = nada a medir.</summary>
    double? TaxaAdesao,
    int? PacientesDistintos,
    double? SessoesPorPaciente,
    /// <summary>Tratamentos CRIADOS na janela (a rota da franquia filtra por criação).</summary>
    int? TratamentosIniciados,
    /// <summary>true = a franquia devolveu o teto de linhas em algum pedaço da janela; números podem estar baixos.</summary>
    bool Incompleto,
    string? Erro);

public record SpineRedeAdesaoTotaisDto(
    int Unidades,
    int SessoesRealizadas,
    int Faltas,
    int Desmarcadas,
    int SemBaixa,
    double? TaxaAdesao,
    int TratamentosIniciados);

/// <summary>Uma semana COMPLETA dentro da janela (segunda a domingo), somando as unidades visíveis.</summary>
public record SpineRedeAdesaoSemanaDto(DateOnly SemanaInicio, int Horarios, int Realizadas);
