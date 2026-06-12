using LeadAnalytics.Api.Models;

namespace LeadAnalytics.Api.Data;

/// <summary>
/// Extensões pra IQueryable&lt;Lead&gt; aplicadas a TODA contagem do dashboard.
///
/// Quando a Kommo envia webhook de <c>delete</c>, o backend só marca
/// <c>lead.Status = "deleted"</c> em vez de remover a linha (idempotência +
/// histórico). Sem esse filtro, esses leads continuavam contando em todos os
/// KPIs e geravam divergência com o número visto na Kommo.
/// </summary>
public static class LeadQueryExtensions
{
    public const string StatusDeleted = "deleted";

    /// <summary>Tira leads marcados como deletados (origem: webhook delete da Kommo).</summary>
    public static IQueryable<Lead> ExcludeDeleted(this IQueryable<Lead> q) =>
        q.Where(l => l.Status != StatusDeleted);
}
