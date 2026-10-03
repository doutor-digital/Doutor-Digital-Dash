using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.DTOs.Spine;
using Microsoft.EntityFrameworkCore;

namespace LeadAnalytics.Api.Service.Spine;

/// <summary>
/// Adesão ao tratamento da rede, unidade por unidade, para o franqueador master e o dono.
///
/// Mesmo desenho do <see cref="SpineRedeService"/>: unidades em paralelo, com isolamento de falha (uma
/// clínica com token revogado vira linha com <c>Erro</c>, não derruba o painel).
///
/// LGPD: a saída é toda agregada e sem identificação. O que identifica alguém — nome, CPF, idClient —
/// nunca entra nos DTOs. Unidade pequena (menos de <see cref="SigiloMinimo"/> pacientes) sai oculta.
/// Dado clínico de evolução NÃO passa por aqui; ver a decisão de 03/10/2026 (dois níveis: agregado na
/// rede, ficha só para a própria unidade, com registro de acesso).
/// </summary>
public class SpineRedeAdesaoService(
    AppDbContext db,
    SpineTokenStore tokens,
    SpineAvaliacoesService avaliacoes,
    FranquiaTratamentosService tratamentos,
    ILogger<SpineRedeAdesaoService> logger)
{
    /// <summary>Menos pacientes que isso na janela e a unidade fica oculta (k-anonimato simples).</summary>
    public const int SigiloMinimo = 5;

    private readonly AppDbContext _db = db;
    private readonly SpineTokenStore _tokens = tokens;
    private readonly SpineAvaliacoesService _avaliacoes = avaliacoes;
    private readonly FranquiaTratamentosService _tratamentos = tratamentos;
    private readonly ILogger<SpineRedeAdesaoService> _logger = logger;

    /// <param name="tenantId">null = super admin, vê todas as unidades ativas.</param>
    public async Task<SpineRedeAdesaoDto> ComparativoAsync(
        int? tenantId, DateOnly de, DateOnly ate, CancellationToken ct = default)
    {
        var q = _db.Units.AsNoTracking().Where(u => u.IsActive);
        if (tenantId.HasValue) q = q.Where(u => u.ClinicId == tenantId.Value);
        var unidades = await q.Select(u => new { u.Id, u.Name }).OrderBy(u => u.Name).ToListAsync(ct);

        var comToken = new List<(int Id, string Nome)>();
        var semToken = new List<SpineRedeSemTokenDto>();
        foreach (var u in unidades)
        {
            var token = await _tokens.GetTokenAsync(u.Id, ct);
            if (string.IsNullOrWhiteSpace(token)) semToken.Add(new SpineRedeSemTokenDto(u.Id, u.Name));
            else comToken.Add((u.Id, u.Name));
        }

        var colhidas = await Task.WhenAll(comToken.Select(async u =>
        {
            try
            {
                var sessoes = await _avaliacoes.GetPorCategoriasAsync(
                    u.Id, de, ate, [SpineApiClient.ScheduleCategory.Sessao], ct);
                if (sessoes is null)
                    return (Linha: Falhou(u.Id, u.Nome, "sem token"), Sessoes: (SpineAvaliacoesDto?)null);

                // Tratamentos vêm de outra rota e podem falhar sozinhos: a agenda já veio e vale mais.
                FranquiaTratamentosDto? trat = null;
                try { trat = await _tratamentos.GetAsync(u.Id, de, ate, ct); }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Adesão da rede: tratamentos da unidade {UnitId} falharam", u.Id);
                }

                return (Linha: MontarUnidade(u.Id, u.Nome, sessoes, trat), Sessoes: sessoes);
            }
            catch (SpineApiException ex)
            {
                _logger.LogWarning(ex, "Adesão da rede: unidade {UnitId} falhou", u.Id);
                return (Linha: Falhou(u.Id, u.Nome, ex.Motivo), Sessoes: null);
            }
            catch (Exception ex)
            {
                // Qualquer outra coisa (timeout, DNS) não pode apagar as demais unidades.
                _logger.LogWarning(ex, "Adesão da rede: unidade {UnitId} falhou (não-Spine)", u.Id);
                return (Linha: Falhou(u.Id, u.Nome, "indisponível agora"), Sessoes: null);
            }
        }));

        var linhas = colhidas.Select(c => c.Linha).ToList();
        var serie = MontarSerieSemanal(colhidas.Where(c => c.Sessoes is not null).Select(c => c.Sessoes!));

        // Pior adesão no topo: é onde o dono precisa olhar. Ocultas e com erro vão para o fim.
        var ordenadas = linhas
            .OrderBy(l => l.Erro is not null)
            .ThenBy(l => l.Oculto)
            .ThenBy(l => l.TaxaAdesao ?? double.MaxValue)
            .ToList();

        return new SpineRedeAdesaoDto(de, ate, SigiloMinimo, ordenadas, semToken, MontarTotais(linhas), serie);
    }

    private static SpineRedeAdesaoUnidadeDto Falhou(int id, string nome, string erro) =>
        new(id, nome, false, null, null, null, null, null, null, null, null, erro);

    /// <summary>Puro: os contadores de uma unidade a partir do card de sessões e dos tratamentos.</summary>
    internal static SpineRedeAdesaoUnidadeDto MontarUnidade(
        int unitId, string nome, SpineAvaliacoesDto sessoes, FranquiaTratamentosDto? tratamentos)
    {
        // Poucos pacientes: nenhum número sai, nem a contagem de pacientes.
        if (sessoes.PacientesDistintos < SigiloMinimo)
            return new SpineRedeAdesaoUnidadeDto(unitId, nome, true, null, null, null, null, null, null, null, null, null);

        int Sit(int idStatus) => sessoes.PorSituacao.FirstOrDefault(s => s.IdStatus == idStatus)?.Total ?? 0;
        var realizadas = Sit(SpineApiClient.ScheduleStatus.Atendido);
        var faltas = Sit(SpineApiClient.ScheduleStatus.NaoCompareceu);
        var desmarcadas = Sit(SpineApiClient.ScheduleStatus.Desmarcado) + Sit(SpineApiClient.ScheduleStatus.Remarcado);

        int? emAndamento = tratamentos?.PorSituacao
            .Where(s => s.Situacao.Contains("ANDAMENTO", StringComparison.OrdinalIgnoreCase))
            .Sum(s => s.Quantidade);

        return new SpineRedeAdesaoUnidadeDto(
            unitId, nome, false,
            realizadas, faltas, desmarcadas,
            Taxa(realizadas, faltas, desmarcadas),
            sessoes.PacientesDistintos,
            Math.Round((double)realizadas / sessoes.PacientesDistintos, 1),
            tratamentos?.Total,
            emAndamento,
            null);
    }

    internal static double Taxa(int realizadas, int faltas, int desmarcadas)
    {
        var resolvidas = realizadas + faltas + desmarcadas;
        return resolvidas == 0 ? 0d : Math.Round((double)realizadas / resolvidas * 100, 1);
    }

    /// <summary>Totais da rede: soma só as unidades que vieram sem erro e SEM sigilo. Unidade oculta não tem número a somar sem revelá-lo; fica fora da soma, mas conta em <c>Unidades</c>.</summary>
    internal static SpineRedeAdesaoTotaisDto MontarTotais(IReadOnlyList<SpineRedeAdesaoUnidadeDto> linhas)
    {
        var ok = linhas.Where(l => l.Erro is null).ToList();
        var visiveis = ok.Where(l => !l.Oculto).ToList();
        var realizadas = visiveis.Sum(l => l.SessoesRealizadas ?? 0);
        var faltas = visiveis.Sum(l => l.Faltas ?? 0);
        var desmarcadas = visiveis.Sum(l => l.Desmarcadas ?? 0);
        return new SpineRedeAdesaoTotaisDto(
            ok.Count, realizadas, faltas, desmarcadas, Taxa(realizadas, faltas, desmarcadas),
            visiveis.Sum(l => l.TratamentosNoPeriodo ?? 0),
            visiveis.Sum(l => l.TratamentosEmAndamento ?? 0));
    }

    /// <summary>Horários e realizados por semana (início na segunda-feira), somando as unidades.</summary>
    internal static IReadOnlyList<SpineRedeAdesaoSemanaDto> MontarSerieSemanal(IEnumerable<SpineAvaliacoesDto> unidades) =>
        unidades
            .Where(u => u.PacientesDistintos >= SigiloMinimo)
            .SelectMany(u => u.PorDia)
            .GroupBy(d => InicioDaSemana(d.Dia))
            .OrderBy(g => g.Key)
            .Select(g => new SpineRedeAdesaoSemanaDto(g.Key, g.Sum(d => d.Total), g.Sum(d => d.Realizadas)))
            .ToList();

    internal static DateOnly InicioDaSemana(DateOnly dia) =>
        dia.AddDays(-(((int)dia.DayOfWeek + 6) % 7));
}
