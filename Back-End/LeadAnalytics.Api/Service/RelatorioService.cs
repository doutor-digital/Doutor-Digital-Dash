using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.DTOs;
using LeadAnalytics.Api.DTOs.Response;
using Microsoft.EntityFrameworkCore;

namespace LeadAnalytics.Api.Service;

/// <summary>
/// Orquestra a geração do relatório mensal:
/// consulta os dados com projeção otimizada, computa KPIs e agrupamentos
/// e delega a renderização ao IPdfRelatorioService.
/// </summary>
public class RelatorioService(AppDbContext db, IPdfRelatorioService pdfService) : IRelatorioService
{
    private static readonly TimeZoneInfo BrazilTz =
        TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");

    private readonly AppDbContext _db = db;
    private readonly IPdfRelatorioService _pdfService = pdfService;

    public async Task<byte[]> GerarRelatorioMensalAsync(int clinicId, int mes, int ano, CancellationToken ct)
    {
        var dados = await ObterResumoMensalAsync(clinicId, mes, ano, ct);
        if (dados is null)
            return [];

        return _pdfService.Gerar(dados);
    }

    public async Task<RelatorioMensalDadosDto?> ObterResumoMensalAsync(int clinicId, int mes, int ano, CancellationToken ct)
    {
        ValidarMesEAno(mes, ano);

        var (inicioUtc, fimUtc) = ObterIntervaloUtc(mes, ano);

        var leads = await _db.Leads
            .AsNoTracking()
            .Where(l =>
                l.TenantId == clinicId &&
                l.CreatedAt >= inicioUtc &&
                l.CreatedAt < fimUtc)
            .Select(l => new LeadProjecaoRelatorio(
                l.Name,
                l.Phone,
                l.Source,
                l.CurrentStage,
                l.UnitId,
                l.HasAppointment,
                l.CreatedAt,
                l.Payments.Sum(p => (decimal?)p.Amount) ?? 0m
            ))
            .ToListAsync(ct);

        if (leads.Count == 0)
            return null;

        var unitIds = leads
            .Where(l => l.UnitId.HasValue)
            .Select(l => l.UnitId!.Value)
            .Distinct()
            .ToList();

        var unidadesMap = unitIds.Count > 0
            ? await _db.Units
                .AsNoTracking()
                .Where(u => u.ClinicId == clinicId && unitIds.Contains(u.Id))
                .Select(u => new { u.Id, u.Name })
                .ToDictionaryAsync(x => x.Id, x => x.Name, ct)
            : [];

        var nomeClinica = unidadesMap.Values.FirstOrDefault() ?? $"Clínica #{clinicId}";

        var totalLeads = leads.Count;

        var totalComConsulta = leads.Count(l => l.HasAppointment);
        var taxaConversao = totalLeads > 0
            ? Math.Round((decimal)totalComConsulta / totalLeads * 100m, 2)
            : 0m;

        var leadsComPagamento = leads.Where(l => l.TotalPago > 0).ToList();
        var ticketMedio = leadsComPagamento.Count > 0
            ? Math.Round(leadsComPagamento.Average(l => l.TotalPago), 2)
            : 0m;

        var leadsPorOrigem = leads
            .GroupBy(l => string.IsNullOrWhiteSpace(l.Origem) ? "Não informado" : l.Origem)
            .Select(g => new OrigemAgrupadaDto
            {
                Origem = g.Key,
                Quantidade = g.Count()
            })
            .OrderByDescending(x => x.Quantidade)
            .ToList();

        var leadsPorEtapa = leads
            .GroupBy(l => string.IsNullOrWhiteSpace(l.Stage) ? "Não informado" : l.Stage)
            .Select(g => new EtapaAgrupadaDto
            {
                Etapa = g.Key,
                Quantidade = g.Count()
            })
            .OrderByDescending(x => x.Quantidade)
            .ToList();

        var leadsPorUnidade = leads
            .GroupBy(l => l.UnitId)
            .Select(g =>
            {
                var nomeUnidade = g.Key.HasValue && unidadesMap.TryGetValue(g.Key.Value, out var nome)
                    ? nome
                    : "Sem unidade";

                return new UnidadeRelatorioDto(
                    g.Key,
                    nomeUnidade,
                    g.Count()
                );
            })
            .OrderByDescending(x => x.QuantidadeLeads)
            .ToList();

        var leadsPorDia = leads
            .GroupBy(l => TimeZoneInfo.ConvertTimeFromUtc(l.CreatedAt, BrazilTz).Day)
            .Select(g => new LeadsPorDiaDto(
                g.Key,
                g.Count()
            ))
            .OrderBy(x => x.Dia)
            .ToList();

        var listaDetalhada = leads
            .Select(l => new LeadRelatorioItemDto(
                l.Nome,
                l.Telefone ?? "Não informado",
                string.IsNullOrWhiteSpace(l.Origem) ? "Não informado" : l.Origem,
                string.IsNullOrWhiteSpace(l.Stage) ? "Não informado" : l.Stage,
                TimeZoneInfo.ConvertTimeFromUtc(l.CreatedAt, BrazilTz)
            ))
            .OrderBy(x => x.CriadoEm)
            .ToList();

        return new RelatorioMensalDadosDto
        {
            NomeClinica = nomeClinica,
            Mes = mes,
            Ano = ano,
            GeradoEm = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, BrazilTz),
            TotalLeads = totalLeads,
            TaxaConversaoPercent = taxaConversao,
            TicketMedio = ticketMedio,
            LeadsPorOrigem = leadsPorOrigem,
            LeadsPorEtapa = leadsPorEtapa,
            LeadsPorUnidade = leadsPorUnidade,
            LeadsPorDia = leadsPorDia,
            Leads = listaDetalhada
        };
    }

    private static void ValidarMesEAno(int mes, int ano)
    {
        if (mes < 1 || mes > 12)
            throw new ArgumentOutOfRangeException(nameof(mes), "O mês deve estar entre 1 e 12.");

        if (ano < 2000 || ano > 2100)
            throw new ArgumentOutOfRangeException(nameof(ano), "O ano informado é inválido.");
    }

    private static (DateTime inicioUtc, DateTime fimUtc) ObterIntervaloUtc(int mes, int ano)
    {
        var inicioLocal = new DateTime(ano, mes, 1, 0, 0, 0, DateTimeKind.Unspecified);
        var fimLocal = inicioLocal.AddMonths(1);

        var inicioUtc = TimeZoneInfo.ConvertTimeToUtc(inicioLocal, BrazilTz);
        var fimUtc = TimeZoneInfo.ConvertTimeToUtc(fimLocal, BrazilTz);

        return (inicioUtc, fimUtc);
    }
}

internal sealed record LeadProjecaoRelatorio(
    string Nome,
    string? Telefone,
    string? Origem,
    string Stage,
    int? UnitId,
    bool HasAppointment,
    DateTime CreatedAt,
    decimal TotalPago
);
