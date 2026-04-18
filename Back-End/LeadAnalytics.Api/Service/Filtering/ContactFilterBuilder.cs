using LeadAnalytics.Api.DTOs.Filter;
using LeadAnalytics.Api.Models;

namespace LeadAnalytics.Api.Service.Filtering;

public static class ContactFilterBuilder
{
    public const int MaxFilters = 20;
    public const int MaxInItems = 200;

    /// <summary>
    /// Aplica a lista de critérios a uma query parametrizada (IQueryable). Valida
    /// whitelist de campo, compatibilidade operador×tipo e formato do valor
    /// antes de tocar no banco.
    /// </summary>
    public static IQueryable<Contact> Apply(IQueryable<Contact> q, List<FilterCriterionDto> filters)
    {
        if (filters.Count > MaxFilters)
            throw new FilterValidationException(
                $"máximo de {MaxFilters} filtros por request (recebido {filters.Count})");

        foreach (var (crit, index) in filters.Select((c, i) => (c, i)))
        {
            if (string.IsNullOrWhiteSpace(crit.Field))
                throw new FilterValidationException($"filtro #{index}: campo ausente");
            if (string.IsNullOrWhiteSpace(crit.Op))
                throw new FilterValidationException($"filtro #{index}: operador ausente", crit.Field);

            if (!ContactFilterRegistry.Fields.TryGetValue(crit.Field, out var def))
                throw new FilterValidationException(
                    $"campo '{crit.Field}' não está na whitelist", crit.Field);

            if (!ContactFilterRegistry.OperatorsByType[def.Type].Contains(crit.Op))
                throw new FilterValidationException(
                    $"operador '{crit.Op}' incompatível com tipo '{def.Type}' em '{crit.Field}'", crit.Field);

            if (!def.Implemented || def.Apply is null)
                throw new FilterNotImplementedException(crit.Field);

            var reader = new ValueReader(crit.Value, crit.Field, MaxInItems);
            q = def.Apply(q, crit.Op, reader);
        }

        return q;
    }
}
