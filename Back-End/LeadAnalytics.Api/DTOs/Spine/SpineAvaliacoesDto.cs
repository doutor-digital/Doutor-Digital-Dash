namespace LeadAnalytics.Api.DTOs.Spine;

/// <summary>
/// Card "Avaliações" — comparecimento real na avaliação, direto da agenda do Spine.
/// É o ponto onde o funil comercial (Kommo) encosta no operacional (Doutor Hérnia).
/// </summary>
/// <param name="Agendadas">Total de avaliações na janela, qualquer status.</param>
/// <param name="Compareceram">idStatus 42 (ATENDIDO).</param>
/// <param name="NaoCompareceram">idStatus 40 (NÃO COMPARECEU).</param>
/// <param name="Desmarcadas">idStatus 57 (DESMARCADO).</param>
/// <param name="Remarcadas">idStatus 41 (REMARCADO).</param>
/// <param name="AguardandoAtendimento">idStatus 37 (AGENDADO) + 38 (CONFIRMADO).</param>
/// <param name="TaxaComparecimento">
/// Compareceram ÷ Agendadas. Denominador proposital: a recepção quase não usa o status
/// NÃO COMPARECEU (1 caso em 30 dias contra 9 desmarcados), então a fórmula clássica
/// atendido ÷ (atendido + faltou) devolveria ~97% — um número falso. Ver
/// <paramref name="AlertaQualidadeDados"/>.
/// </param>
/// <param name="PacientesDistintos">Nomes distintos na janela — proxy de pessoas, não de horários.</param>
/// <param name="AlertaQualidadeDados">
/// true quando há desmarques mas quase nenhum no-show registrado, indicando que a
/// recepção usa DESMARCADO como categoria guarda-chuva.
/// </param>
public record SpineAvaliacoesDto(
    DateOnly De,
    DateOnly Ate,
    int Agendadas,
    int Compareceram,
    int NaoCompareceram,
    int Desmarcadas,
    int Remarcadas,
    int AguardandoAtendimento,
    double TaxaComparecimento,
    int PacientesDistintos,
    bool AlertaQualidadeDados,
    IReadOnlyList<SpineAvaliacoesPorDiaDto> PorDia,
    IReadOnlyList<SpineAvaliacoesPorProfissionalDto> PorProfissional);

public record SpineAvaliacoesPorDiaDto(DateOnly Dia, int Agendadas, int Compareceram);

public record SpineAvaliacoesPorProfissionalDto(string Profissional, int Atendimentos);
