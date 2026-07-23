namespace LeadAnalytics.Api.DTOs.Spine;

/// <summary>
/// Um horário da agenda da clínica, já em horário local e com a categoria
/// resolvida.
///
/// A categoria não vem na resposta de /schedules/search — é descoberta pedindo
/// uma vez por <c>idCategory</c> e carimbando o resultado. Por isso ela existe
/// aqui e não no <see cref="SpineSchedule"/> cru.
/// </summary>
/// <param name="Inicio">Início no fuso da clínica (a API devolve UTC).</param>
/// <param name="Grupo">realizado | falta | cancelado | pendente — o desfecho.</param>
public record SpineAgendaItemDto(
    long IdSchedule,
    long? IdTreatment,
    string Paciente,
    DateTime Inicio,
    int IdCategoria,
    string Categoria,
    string Profissional,
    int IdStatus,
    string Status,
    string Grupo);

/// <param name="Categorias">Legenda: categorias presentes na janela.</param>
public record SpineAgendaDto(
    DateOnly De,
    DateOnly Ate,
    int Total,
    IReadOnlyList<string> Categorias,
    IReadOnlyList<SpineAgendaItemDto> Itens);
