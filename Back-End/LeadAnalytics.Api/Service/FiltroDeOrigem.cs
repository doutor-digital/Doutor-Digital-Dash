using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace LeadAnalytics.Api.Service;

/// <summary>
/// O filtro "Origem" do dashboard: restringe os leads aos que têm o ⚑ Origem do cartão
/// igual ao escolhido.
///
/// POR QUE ESTA CLASSE EXISTE
/// --------------------------
/// A lista de opções do filtro sai do campo do cartão (<see cref="LeadService.GetDistinctSourcesAsync"/>:
/// Meta-Instagram, Meta-Facebook, Indicação…), mas a conta filtrava a coluna <c>leads.Source</c>,
/// que vale "Kommo" em 100% dos leads (104.293 de 104.293 em 07/10/2026). Escolher
/// "Meta-Instagram" zerava os gráficos de baixo e não mexia nos cards de cima — a tela
/// respondia com a resposta de outra pergunta.
///
/// A REGRA É A MESMA DA LISTA DE OPÇÕES, DE PROPÓSITO
/// --------------------------------------------------
/// No Postgres, o SQL abaixo repete o de <c>GetDistinctSourcesAsync</c>: campo cujo nome, sem
/// os símbolos ⚑⌂☎ e espaços, é "origem"; valor comparado depois do <c>btrim</c>. Toda opção que
/// a lista oferece casa com pelo menos um lead — se as duas regras divergirem, o filtro volta a
/// zerar a tela. Mexeu numa, mexa na outra.
///
/// O filtro roda no banco (subconsulta por id), não na memória: o cartão de uma unidade grande
/// passa de 25 MB de JSON, e trazê-lo inteiro a cada atualização da tela (30 s) seria caro. Fora
/// do Postgres — os testes usam o banco em memória — a mesma regra roda em C#, via
/// <see cref="OrigemDoLead"/>.
/// </summary>
public static class FiltroDeOrigem
{
    /// <summary>
    /// Devolve <paramref name="query"/> restrita aos leads com ⚑ Origem = <paramref name="origem"/>.
    /// Origem vazia não filtra. <paramref name="tenantId"/> e <paramref name="unitId"/> só
    /// estreitam a subconsulta (desempenho); quem recorta o resultado continua sendo a query.
    /// </summary>
    public static async Task<IQueryable<Lead>> AplicarAsync(
        AppDbContext db, IQueryable<Lead> query, int? tenantId, int? unitId, string? origem,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(origem)) return query;
        var alvo = origem.Trim();

        if (db.Database.IsNpgsql())
        {
            var comOrigem = IdsComOrigem(db, tenantId, unitId, alvo);
            return query.Where(l => comOrigem.Contains(l.Id));
        }

        // Banco em memória (testes): mesma regra, em C#.
        var linhas = await query
            .Where(l => l.CustomFieldsJson != null)
            .Select(l => new { l.Id, l.CustomFieldsJson })
            .ToListAsync(ct);
        var ids = linhas
            .Where(x => string.Equals(OrigemDoLead.Ler(x.CustomFieldsJson), alvo, StringComparison.Ordinal))
            .Select(x => x.Id)
            .ToList();
        return query.Where(l => ids.Contains(l.Id));
    }

    /// <summary>
    /// A subconsulta do Postgres. Pública para o teste conferir o SQL gerado sem precisar de banco.
    /// <c>jsonb_array_elements</c> recebe "[]" quando o cartão não é uma lista: sem isso, um único
    /// cartão torto derrubaria a consulta inteira com erro.
    /// </summary>
    public static IQueryable<int> IdsComOrigem(AppDbContext db, int? tenantId, int? unitId, string alvo)
    {
        // A coluna precisa se chamar "Value": é o nome que o SqlQuery escalar do EF procura.
        // Tenant e unidade entram por concatenação (com parâmetro), pelo mesmo motivo de
        // GetDistinctSourcesAsync: um NULL sem tipo faz o Npgsql recusar o parâmetro.
        var parametros = new List<object> { alvo };
        var escopo = "";
        if (tenantId.HasValue)
        {
            escopo += $@" and l.""TenantId"" = {{{parametros.Count}}}";
            parametros.Add(tenantId.Value);
        }
        if (unitId.HasValue)
        {
            escopo += $@" and l.""UnitId"" = {{{parametros.Count}}}";
            parametros.Add(unitId.Value);
        }

        var sql = $@"
            select l.""Id"" as ""Value""
            from leads l
            where l.""CustomFieldsJson"" is not null{escopo}
              and exists (
                  select 1
                  from jsonb_array_elements(
                      case when jsonb_typeof(l.""CustomFieldsJson"") = 'array'
                           then l.""CustomFieldsJson"" else '[]'::jsonb end) e(value)
                  where btrim(lower(e.value->>'field_name'), '⚑⌂☎ ') = 'origem'
                    and btrim(e.value->>'value') = {{0}})";

        return db.Database.SqlQueryRaw<int>(sql, parametros.ToArray());
    }
}
