using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.DTOs.Response;
using Microsoft.EntityFrameworkCore;

namespace LeadAnalytics.Api.Service;

/// <summary>
/// Fechamento do dia por unidade.
///
/// TRÊS ERROS QUE ESTA VERSÃO CORRIGE
/// ----------------------------------
/// 1. O total contava ATRIBUIÇÕES, não leads: partia de LeadAssignments filtrado por
///    AssignedAt. Lead sem responsável não entrava — e hoje NENHUM lead novo tem
///    responsável (0 de 449 nos últimos 40 dias da unidade 15) —, enquanto lead
///    reatribuído entrava duas vezes. O relatório saía praticamente vazio.
/// 2. Não tinha origem. A coluna Source está preenchida em 100% dos leads recentes; a
///    informação estava ali o tempo todo e o relatório nunca a mostrou.
/// 3. O motivo do não agendamento era adivinhado por busca de palavra em texto livre
///    ("sem tempo", "depois"…). O motivo existe como campo próprio na Kommo. Heurística
///    de texto inventa categoria e ninguém consegue conferir depois.
///
/// AS ETAPAS SÃO DUAS FAMÍLIAS AO MESMO TEMPO
/// ------------------------------------------
/// A base tem funil legado (04_/09_/10_) e novo (QUALIFICACAO/NEGOCIACAO/PERDIDO)
/// convivendo na mesma unidade, mais leads com o id numérico da etapa não resolvido.
/// Por isso agendamento/pagamento não podem sair só de comparar string de etapa: usam
/// primeiro os campos tipados (HasAppointment, AppointmentScheduledAt) e a etapa como
/// reforço.
///
/// AS PENDÊNCIAS FAZEM PARTE DO RELATÓRIO, NÃO SÃO EXTRA
/// -----------------------------------------------------
/// Sem elas, "3 agendamentos" parece um dia ruim quando pode ser 3 preenchidos de 12.
/// Um relatório que não diz o que falta não é conferível.
/// </summary>
public class DailyRelatoryService(AppDbContext db)
{
    private readonly AppDbContext _db = db;

    private static readonly string[] EtapasAgendado =
        [LeadStages.AgendadoSemPagamento, LeadStages.AgendadoComPagamento];

    private static readonly string[] EtapasPagou =
        [LeadStages.FechouTratamento, LeadStages.EmTratamento];

    private static bool Vazio(string? s) =>
        string.IsNullOrWhiteSpace(s) || s.Trim().Equals("n/a", StringComparison.OrdinalIgnoreCase);

    public async Task<List<DailyRelatoryDto>> GenerateDailyRelatory(int tenantId, DateTime date)
    {
        var tz = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");

        var inicioDia = TimeZoneInfo.ConvertTimeToUtc(
            new DateTime(date.Year, date.Month, date.Day, 0, 0, 0, DateTimeKind.Unspecified), tz);
        var fimDia = TimeZoneInfo.ConvertTimeToUtc(
            new DateTime(date.Year, date.Month, date.Day, 23, 59, 59, DateTimeKind.Unspecified), tz);

        // Parte dos LEADS do dia. É a correção que muda o total.
        var leads = await _db.Leads.AsNoTracking()
            .Include(l => l.Unit)
            .Where(l => l.TenantId == tenantId
                        && l.Unit != null
                        && l.CreatedAt >= inicioDia && l.CreatedAt <= fimDia)
            .Select(l => new
            {
                l.Id, l.UnitId, UnidadeNome = l.Unit!.Name, l.Name, l.CurrentStage,
                l.Source, l.Qualification, l.LeadType, l.Tags, l.Observations,
                l.HasAppointment, l.AppointmentScheduledAt, l.NoAppointmentReason,
                l.ClosedTreatment, l.AttendantId,
            })
            .ToListAsync();

        if (leads.Count == 0) return [];

        var nomesAtendentes = await _db.Attendants.AsNoTracking()
            .Where(a => leads.Select(l => l.AttendantId).Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, a => a.Name);

        return [.. leads
            .GroupBy(l => new { l.UnitId, l.UnidadeNome })
            .Select(g =>
            {
                var total = g.Count();

                var agendou = g.Where(l =>
                    l.HasAppointment
                    || l.AppointmentScheduledAt != null
                    || EtapasAgendado.Contains(l.CurrentStage ?? "")).ToList();

                var naoAgendou = g.Where(l => !agendou.Select(a => a.Id).Contains(l.Id)).ToList();

                return new DailyRelatoryDto
                {
                    Unidade = g.Key.UnidadeNome,
                    UnidadeId = g.Key.UnitId ?? 0,
                    TotalLeads = total,
                    Agendamentos = agendou.Count,
                    ComPagamento = g.Count(l =>
                        EtapasPagou.Contains(l.CurrentStage ?? "") || l.ClosedTreatment == true),
                    Resgastes = g.Count(l =>
                        (l.LeadType ?? "").Contains("resgate", StringComparison.OrdinalIgnoreCase)
                        || (l.Tags ?? "").Contains("resgate", StringComparison.OrdinalIgnoreCase)),

                    PorOrigem = Contagem(g.Select(l => l.Source), total, "Sem origem"),
                    PorQualificacao = Contagem(g.Select(l => l.Qualification), total, "Sem qualificação"),
                    MotivosNaoAgendamento = Contagem(
                        naoAgendou.Select(l => l.NoAppointmentReason), naoAgendou.Count, "Sem motivo informado"),

                    Pendencias = Pendencias(
                        total,
                        semOrigem: g.Count(l => Vazio(l.Source)),
                        semQualificacao: g.Count(l => Vazio(l.Qualification)),
                        agendadoSemData: agendou.Count(l => l.AppointmentScheduledAt is null),
                        naoAgendouSemMotivo: naoAgendou.Count(l => Vazio(l.NoAppointmentReason)),
                        semResponsavel: g.Count(l => l.AttendantId is null)),

                    Atendentes = [.. g.Where(l => l.AttendantId != null)
                        .Select(l => nomesAtendentes.TryGetValue(l.AttendantId!.Value, out var n) ? n : null)
                        .Where(n => n is not null).Select(n => n!).Distinct().OrderBy(n => n)],

                    Observacoes = string.Join(" | ", g
                        .Where(l => !Vazio(l.Observations))
                        .Take(40)
                        .Select(l =>
                        {
                            var nome = Vazio(l.Name) ? "Sem nome" : l.Name;
                            var ag = agendou.Any(a => a.Id == l.Id) ? "Agendou" : "Não agendou";
                            // O motivo vem do CAMPO. Antes era heurística de palavra no texto
                            // livre, que inventava categoria e não dava para conferir.
                            var motivo = Vazio(l.NoAppointmentReason) ? "Não informado" : l.NoAppointmentReason;
                            return $"{nome} — {ag} — Motivo: {motivo}";
                        })),
                };
            })
            .OrderByDescending(x => x.TotalLeads)];
    }

    /// <summary>Agrupa e ordena, com rótulo próprio para o vazio em vez de sumir com ele.</summary>
    private static List<RelatorioContagemDto> Contagem(
        IEnumerable<string?> valores, int total, string rotuloVazio) =>
        [.. valores
            .Select(v => Vazio(v) ? rotuloVazio : v!.Trim())
            .GroupBy(v => v, StringComparer.OrdinalIgnoreCase)
            .Select(g => new RelatorioContagemDto
            {
                Rotulo = g.Key,
                Quantidade = g.Count(),
                Percentual = total == 0 ? 0 : Math.Round(100.0 * g.Count() / total, 1),
            })
            .OrderByDescending(c => c.Quantidade)];

    private static List<RelatorioPendenciaDto> Pendencias(
        int total, int semOrigem, int semQualificacao, int agendadoSemData,
        int naoAgendouSemMotivo, int semResponsavel)
    {
        var lista = new List<RelatorioPendenciaDto>
        {
            new() { Campo = "Origem", Quantidade = semOrigem,
                    Impacto = "sem ela o lead não entra em nenhuma quebra por canal" },
            new() { Campo = "Qualificação", Quantidade = semQualificacao,
                    Impacto = "é o que separa quem vale insistir de quem não vale" },
            new() { Campo = "Data de agendamento", Quantidade = agendadoSemData,
                    Impacto = "agendado sem data não entra em lembrete nem no card do dia" },
            new() { Campo = "Motivo do não agendamento", Quantidade = naoAgendouSemMotivo,
                    Impacto = "sem motivo não há o que atacar na semana seguinte" },
            new() { Campo = "Responsável", Quantidade = semResponsavel,
                    Impacto = "lead sem dono não aparece em ranking e ninguém é cobrado por ele" },
        };
        return [.. lista.Where(p => p.Quantidade > 0).OrderByDescending(p => p.Quantidade)];
    }
}
