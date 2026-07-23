namespace LeadAnalytics.Api.DTOs.Spine;

/// <summary>
/// Comparativo entre unidades da rede — o painel que o franqueador master vê.
/// Uma linha por unidade com token configurado, ordenável por qualquer métrica.
/// </summary>
public record SpineRedeDto(
    DateOnly De,
    DateOnly Ate,
    IReadOnlyList<SpineRedeUnidadeDto> Unidades,
    /// <summary>Unidades do tenant que ainda não conectaram o Doutor Hérnia.</summary>
    IReadOnlyList<SpineRedeSemTokenDto> SemToken,
    /// <summary>Totais consolidados da rede (só das unidades com token).</summary>
    SpineRedeTotaisDto Totais);

public record SpineRedeUnidadeDto(
    int UnitId,
    string Unidade,
    int Agendadas,
    int Compareceram,
    int NaoCompareceram,
    int Desmarcadas,
    double TaxaComparecimento,
    int PacientesDistintos,
    /// <summary>Falha ao consultar essa unidade (token revogado etc.), se houver.</summary>
    string? Erro);

public record SpineRedeSemTokenDto(int UnitId, string Unidade);

public record SpineRedeTotaisDto(
    int Unidades,
    int Agendadas,
    int Compareceram,
    double TaxaComparecimento);
