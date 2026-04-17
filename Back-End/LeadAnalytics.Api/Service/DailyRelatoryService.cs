using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.DTOs.Response;
using Microsoft.EntityFrameworkCore;

namespace LeadAnalytics.Api.Service;

public class DailyRelatoryService(AppDbContext db)
{
    private readonly AppDbContext _db = db;
    public async Task<List<DailyRelatoryDto>> GenerateDailyRelatory(int tenantId, DateTime date)
    {
        var tz = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");

        var inicioDia = TimeZoneInfo.ConvertTimeToUtc(
            new DateTime(date.Year, date.Month, date.Day, 0, 0, 0, DateTimeKind.Unspecified), tz);

        var fimDia = TimeZoneInfo.ConvertTimeToUtc(
            new DateTime(date.Year, date.Month, date.Day, 23, 59, 59, DateTimeKind.Unspecified), tz);

        var assignment = await _db.LeadAssignments
            .Include(a => a.Attendant)
            .Include(a => a.Lead).ThenInclude(u => u.Unit)
            .Where(a =>
                a.Lead.TenantId == tenantId &&
                a.AssignedAt >= inicioDia &&
                a.AssignedAt <= fimDia)
            .ToListAsync();

        return [.. assignment
        .Where(a => a.Lead.Unit != null)
        .GroupBy(a => new { a.Lead.UnitId, a.Lead.Unit!.Name })
        .Select(g => new DailyRelatoryDto
            {
                Unidade = g.Key.Name,
                TotalLeads = g.Count(),
                Agendamentos = g.Count(a => PossuiAgendamento(a.Lead.CurrentStage)),
                ComPagamento = g.Count(a => PossuiPagamento(a.Lead.CurrentStage)),
                Resgastes = g.Count(a => PossuiResgate(a.Lead.Tags)),
                Observacoes = string.Join(" | ", g
                .Where(a => a.Lead.Observations != null)
                .Select(a =>
                {
                    var nome = a.Lead.Name ?? "Sem nome";

                    var agendou = PossuiAgendamento(a.Lead.CurrentStage);

                    var motivo = ExtrairMotivo(a.Lead.Observations);

                    return $"{nome} — {(agendou ? "Agendou" : "Não agendou")} — Motivo: {motivo}";
                })),

                Atendentes = [.. g
                    .Select(a => a.Attendant.Name)
                    .Distinct()]
            })
        .OrderByDescending(x => x.TotalLeads)];
        }

    private static bool PossuiPagamento(string? stage)
    {
        return stage == "10_EM_TRATAMENTO"
            || stage == "09_FECHOU_TRATAMENTO";
    }
    private static bool PossuiAgendamento(string? stage)
    {
        return stage == "04_AGENDADO_SEM_PAGAMENTO"
            || stage == "05_AGENDADO_COM_PAGAMENTO";
    }
    private static bool PossuiResgate(string? tags)
    {
        return tags != null && tags.Contains("resgate-lead");
    }

    private static string ExtrairMotivo(string? obs)
    {
        if (string.IsNullOrEmpty(obs))
            return "Não informado";

        var o = obs.ToLower();

        if (o.Contains("sem tempo"))
            return "Sem tempo";

        if (o.Contains("sem interesse"))
            return "Sem interesse";

        if (o.Contains("numero errado") || o.Contains("inválido"))
            return "Contato inválido";

        if (o.Contains("depois") || o.Contains("retorno"))
            return "Pediu retorno";

        if (o.Contains("interessado"))
            return "Interessado";

        return "Outro";
    }
}
