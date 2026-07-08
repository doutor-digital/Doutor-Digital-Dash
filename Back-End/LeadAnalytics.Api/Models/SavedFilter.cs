namespace LeadAnalytics.Api.Models;

/// <summary>
/// Filtro dinâmico salvo pelo analista e exibido como uma "pílula" no topo do dashboard,
/// para todas as unidades (é global). Guarda uma combinação completa de filtros —
/// período, faixa de horas, origem, atendente, responsável e etapas — sob um nome.
///
/// Ao clicar na pílula, o front restaura todos esses filtros de uma vez, para que as
/// clínicas não precisem montar o filtro na mão em "Filtros avançados".
///
/// Só analista_ti / super_admin criam, editam e removem; qualquer usuário logado lê.
/// </summary>
public class SavedFilter
{
    public int Id { get; set; }

    /// <summary>Nome exibido na pílula (ex.: "Comercial hoje", "Mês — origem Facebook").</summary>
    public string Name { get; set; } = null!;

    /// <summary>
    /// Payload completo do filtro, em JSON. Espelha o estado da tela:
    /// <c>{"rangeKey":"dia","customFrom":"","customTo":"","customFromTime":"07:00",
    /// "customToTime":"19:00","sourceFilter":"","attendantFilter":"","responsibleFilter":"",
    /// "stageFilter":["104945887"]}</c>.
    /// </summary>
    public string FilterJson { get; set; } = "{}";

    /// <summary>Ordem de exibição entre as pílulas (menor primeiro).</summary>
    public int SortOrder { get; set; }

    /// <summary>E-mail do analista que salvou por último (auditoria leve).</summary>
    public string? UpdatedByEmail { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
