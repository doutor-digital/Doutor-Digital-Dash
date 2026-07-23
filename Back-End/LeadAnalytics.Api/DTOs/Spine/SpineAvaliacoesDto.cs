namespace LeadAnalytics.Api.DTOs.Spine;

/// <summary>
/// Card "Avaliações" — o que aconteceu com cada horário de avaliação na janela,
/// direto da agenda do Doutor Hérnia. É o ponto onde o funil comercial (Kommo)
/// encosta no operacional.
/// </summary>
/// <param name="Realizadas">
/// idStatus 42 (ATENDIDO). É o número que a franquia chama de "Avaliações" no
/// painel dela — conferido em 23/07/2026: 25 de um lado, 25 do outro.
/// </param>
/// <param name="Total">Todos os horários da janela, em qualquer situação.</param>
/// <param name="Resolvidas">
/// Total menos os que ainda não aconteceram (AGENDADO + CONFIRMADO). É o
/// denominador da taxa: horário de amanhã não conta como falha nem como acerto.
/// </param>
/// <param name="TaxaComparecimento">
/// Realizadas ÷ Resolvidas. Desmarcado entra no denominador de propósito — é
/// agenda que a clínica reservou e não usou. A franquia calcula diferente
/// (ignora desmarque); a diferença está documentada no card.
/// </param>
/// <param name="AlertaQualidadeDados">
/// true quando há desmarques mas quase nenhum no-show registrado, indicando que
/// a recepção usa DESMARCADO como categoria guarda-chuva.
/// </param>
public record SpineAvaliacoesDto(
    DateOnly De,
    DateOnly Ate,
    int Total,
    int Realizadas,
    int Resolvidas,
    double TaxaComparecimento,
    int PacientesDistintos,
    bool AlertaQualidadeDados,
    IReadOnlyList<SpineSituacaoDto> PorSituacao,
    IReadOnlyList<SpineAvaliacoesPorDiaDto> PorDia,
    IReadOnlyList<SpineAvaliacoesPorProfissionalDto> PorProfissional);

/// <summary>
/// Uma das seis situações da agenda. O Spine não expõe endpoint que liste esses
/// códigos — o mapa foi levantado por amostragem.
/// </summary>
/// <param name="Grupo">
/// "realizado" | "falta" | "cancelado" | "pendente" — agrupa as seis situações
/// nos quatro desfechos que o negócio enxerga, para o front colorir sem
/// reimplementar a regra.
/// </param>
public record SpineSituacaoDto(int IdStatus, string Nome, string Grupo, int Total);

public record SpineAvaliacoesPorDiaDto(DateOnly Dia, int Total, int Realizadas);

public record SpineAvaliacoesPorProfissionalDto(string Profissional, int Atendimentos);
