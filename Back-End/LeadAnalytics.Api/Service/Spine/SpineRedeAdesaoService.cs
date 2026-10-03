using System.Globalization;
using System.Text;
using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.DTOs.Spine;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace LeadAnalytics.Api.Service.Spine;

/// <summary>
/// Adesão ao tratamento da rede, unidade por unidade, para o franqueador master e o dono.
///
/// Desenho:
///  - os tokens são lidos do banco ANTES da parte paralela, um a um: o AppDbContext é scoped e não aceita
///    duas consultas ao mesmo tempo. A parte paralela só fala HTTP com a franquia (sem banco);
///  - cada unidade é isolada: falhou, vira linha com <c>Erro</c> e as outras seguem;
///  - a agenda de sessões é lida em pedaços de até 30 dias: a busca da franquia para em 40 páginas de 100
///    e uma unidade movimentada passa disso em 90 dias.
///
/// LGPD: saída toda agregada. Nome de paciente só é usado DENTRO do cálculo, para contar pessoas distintas
/// (o sigilo), e nunca sai. Ver <see cref="SpineRedeAdesaoDto"/>.
/// </summary>
public class SpineRedeAdesaoService(
    AppDbContext db,
    SpineTokenStore tokens,
    SpineApiClient client,
    IMemoryCache cache,
    ILogger<SpineRedeAdesaoService> logger)
{
    /// <summary>Menos pacientes que isso na janela e a unidade fica oculta (k-anonimato simples).</summary>
    public const int SigiloMinimo = 5;

    /// <summary>As únicas janelas aceitas. Ver o motivo em <see cref="SpineRedeAdesaoDto"/>.</summary>
    public static readonly int[] JanelasPermitidas = [30, 60, 90];

    private const int DiasPorPedaco = 30;
    /// <summary>40 páginas × 100 linhas: o teto da busca da franquia. Chegar perto = provavelmente cortou.</summary>
    private const int TetoDeLinhas = 40 * SpineApiClient.MaxRowsPerPage;

    private readonly AppDbContext _db = db;
    private readonly SpineTokenStore _tokens = tokens;
    private readonly SpineApiClient _client = client;
    private readonly IMemoryCache _cache = cache;
    private readonly ILogger<SpineRedeAdesaoService> _logger = logger;

    /// <summary>Janela fixa terminando ONTEM no horário de Brasília (hoje ainda está acontecendo).</summary>
    public static (DateOnly De, DateOnly Ate) Janela(int dias, DateTime agoraUtc)
    {
        var ontem = SpineApiClient.DiaLocal(agoraUtc).AddDays(-1);
        return (ontem.AddDays(-(dias - 1)), ontem);
    }

    /// <param name="tenantId">null = super admin, vê todas as unidades ativas.</param>
    public async Task<SpineRedeAdesaoDto> ComparativoAsync(int? tenantId, int dias, CancellationToken ct = default)
    {
        var agora = DateTime.UtcNow;
        var (de, ate) = Janela(dias, agora);

        var q = _db.Units.AsNoTracking().Where(u => u.IsActive);
        if (tenantId.HasValue) q = q.Where(u => u.ClinicId == tenantId.Value);
        var unidades = await q.Select(u => new { u.Id, u.Name }).OrderBy(u => u.Name).ToListAsync(ct);

        // Banco: em sequência. Nada de AppDbContext daqui pra baixo.
        var comToken = new List<(int Id, string Nome, string Token)>();
        var semToken = new List<SpineRedeSemTokenDto>();
        foreach (var u in unidades)
        {
            var token = await _tokens.GetTokenAsync(u.Id, ct);
            if (string.IsNullOrWhiteSpace(token)) semToken.Add(new SpineRedeSemTokenDto(u.Id, u.Name));
            else comToken.Add((u.Id, u.Name, token));
        }

        var colhidas = await Task.WhenAll(comToken.Select(async u =>
        {
            try
            {
                var (linhas, incompleto) = await SessoesAsync(u.Id, u.Token, de, ate, ct);

                // Tratamentos vêm de outra rota e podem falhar sozinhos: a agenda já veio e vale mais.
                int? iniciados = null;
                try { iniciados = (await TratamentosAsync(u.Id, u.Token, de, ate, ct)).Count; }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Adesão da rede: tratamentos da unidade {UnitId} falharam", u.Id);
                }

                return (Linha: MontarUnidade(u.Id, u.Nome, linhas, iniciados, incompleto), Linhas: linhas);
            }
            catch (SpineApiException ex)
            {
                _logger.LogWarning(ex, "Adesão da rede: unidade {UnitId} falhou", u.Id);
                return (Linha: Falhou(u.Id, u.Nome, ex.Motivo), Linhas: (IReadOnlyList<SpineSchedule>?)null);
            }
            catch (Exception ex)
            {
                // Timeout, DNS: uma clínica com problema não pode apagar as outras.
                _logger.LogWarning(ex, "Adesão da rede: unidade {UnitId} falhou (não-Spine)", u.Id);
                return (Linha: Falhou(u.Id, u.Nome, "indisponível agora"), Linhas: null);
            }
        }));

        var linhasDasUnidades = colhidas.Select(c => c.Linha).ToList();
        var serie = MontarSerieSemanal(
            colhidas.Where(c => c.Linhas is not null && !c.Linha.Oculto).Select(c => c.Linhas!), de, ate);

        // Pior adesão no topo: é onde o dono precisa olhar. Sem medida, ocultas e com erro vão para o fim.
        var ordenadas = linhasDasUnidades
            .OrderBy(l => l.Erro is not null)
            .ThenBy(l => l.Oculto)
            .ThenBy(l => l.TaxaAdesao is null)
            .ThenBy(l => l.TaxaAdesao ?? 0)
            .ToList();

        return new SpineRedeAdesaoDto(de, ate, dias, SigiloMinimo, ordenadas, semToken, MontarTotais(linhasDasUnidades), serie);
    }

    private async Task<(IReadOnlyList<SpineSchedule> Linhas, bool Incompleto)> SessoesAsync(
        int unitId, string token, DateOnly de, DateOnly ate, CancellationToken ct)
    {
        var chave = $"spine:adesao:sessoes:{unitId}:{de:yyyyMMdd}:{ate:yyyyMMdd}";
        if (_cache.TryGetValue(chave, out (IReadOnlyList<SpineSchedule>, bool) hit)) return hit;

        var vistos = new HashSet<long>();
        var todas = new List<SpineSchedule>();
        var incompleto = false;
        for (var ini = de; ini <= ate; ini = ini.AddDays(DiasPorPedaco))
        {
            var fim = ini.AddDays(DiasPorPedaco - 1);
            if (fim > ate) fim = ate;
            var parte = await _client.SearchSchedulesAsync(token, ini, fim, SpineApiClient.ScheduleCategory.Sessao, ct);
            if (parte.Count >= TetoDeLinhas - SpineApiClient.MaxRowsPerPage) incompleto = true;
            foreach (var r in parte) if (vistos.Add(r.IdSchedule)) todas.Add(r);
        }

        var res = ((IReadOnlyList<SpineSchedule>)todas, incompleto);
        _cache.Set(chave, res, TimeSpan.FromMinutes(10));
        return res;
    }

    private async Task<IReadOnlyList<SpineTreatment>> TratamentosAsync(
        int unitId, string token, DateOnly de, DateOnly ate, CancellationToken ct)
    {
        var chave = $"spine:adesao:trat:{unitId}:{de:yyyyMMdd}:{ate:yyyyMMdd}";
        if (_cache.TryGetValue(chave, out IReadOnlyList<SpineTreatment>? hit) && hit is not null) return hit;
        var linhas = await _client.SearchTreatmentsAsync(token, de, ate, ct);
        _cache.Set(chave, linhas, TimeSpan.FromMinutes(10));
        return linhas;
    }

    private static SpineRedeAdesaoUnidadeDto Falhou(int id, string nome, string erro) =>
        new(id, nome, false, null, null, null, null, null, null, null, null, false, erro);

    /// <summary>Linha que entra na conta: horário que já tem (ou deveria ter) desfecho.</summary>
    internal static bool Conta(SpineSchedule s) =>
        s.DateAttendance is not null && s.IdStatus is
            SpineApiClient.ScheduleStatus.Atendido or
            SpineApiClient.ScheduleStatus.NaoCompareceu or
            SpineApiClient.ScheduleStatus.Desmarcado or
            SpineApiClient.ScheduleStatus.Agendado or
            SpineApiClient.ScheduleStatus.Confirmado;

    /// <summary>
    /// Puro: os contadores de uma unidade a partir das sessões da janela (que termina ONTEM, então todo
    /// horário dela já passou). Remarcada fica fora: ela gera outro horário, que conta por si.
    /// </summary>
    internal static SpineRedeAdesaoUnidadeDto MontarUnidade(
        int unitId, string nome, IReadOnlyList<SpineSchedule> sessoes, int? tratamentosIniciados, bool incompleto)
    {
        var conta = sessoes.Where(Conta).ToList();
        var pacientes = conta.Select(s => NomeNormalizado(s.ClientName)).Where(n => n.Length > 0).Distinct().Count();

        // Poucos pacientes: nenhum número sai, nem a contagem de pacientes.
        if (pacientes < SigiloMinimo)
            return new SpineRedeAdesaoUnidadeDto(unitId, nome, true, null, null, null, null, null, null, null, null, false, null);

        int Status(params int[] ids) => conta.Count(s => ids.Contains(s.IdStatus));
        var realizadas = Status(SpineApiClient.ScheduleStatus.Atendido);
        var faltas = Status(SpineApiClient.ScheduleStatus.NaoCompareceu);
        var desmarcadas = Status(SpineApiClient.ScheduleStatus.Desmarcado);
        var semBaixa = Status(SpineApiClient.ScheduleStatus.Agendado, SpineApiClient.ScheduleStatus.Confirmado);

        return new SpineRedeAdesaoUnidadeDto(
            unitId, nome, false,
            realizadas, faltas, desmarcadas, semBaixa,
            Taxa(realizadas, faltas, desmarcadas, semBaixa),
            pacientes,
            Math.Round((double)realizadas / pacientes, 1),
            tratamentosIniciados,
            incompleto,
            null);
    }

    /// <summary>null quando não há nada a medir — 0% pintaria de vermelho uma unidade sem sessão.</summary>
    internal static double? Taxa(int realizadas, int faltas, int desmarcadas, int semBaixa)
    {
        var base_ = realizadas + faltas + desmarcadas + semBaixa;
        return base_ == 0 ? null : Math.Round((double)realizadas / base_ * 100, 1);
    }

    /// <summary>"Maria  Souza " e "MARIA SOUZA" são a mesma pessoa para o sigilo. Acento e caixa não separam gente.</summary>
    internal static string NomeNormalizado(string? nome)
    {
        var s = (nome ?? string.Empty).Trim().ToUpperInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(s.Length);
        var espaco = false;
        foreach (var c in s)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsWhiteSpace(c)) { if (!espaco && sb.Length > 0) sb.Append(' '); espaco = true; continue; }
            espaco = false;
            sb.Append(c);
        }
        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Totais da rede: só unidades sem erro e SEM sigilo (oculta não tem número a somar sem revelá-lo), mas
    /// todas as sem erro contam em <c>Unidades</c>.
    /// </summary>
    internal static SpineRedeAdesaoTotaisDto MontarTotais(IReadOnlyList<SpineRedeAdesaoUnidadeDto> linhas)
    {
        var ok = linhas.Where(l => l.Erro is null).ToList();
        var visiveis = ok.Where(l => !l.Oculto).ToList();
        var realizadas = visiveis.Sum(l => l.SessoesRealizadas ?? 0);
        var faltas = visiveis.Sum(l => l.Faltas ?? 0);
        var desmarcadas = visiveis.Sum(l => l.Desmarcadas ?? 0);
        var semBaixa = visiveis.Sum(l => l.SemBaixa ?? 0);
        return new SpineRedeAdesaoTotaisDto(
            ok.Count, realizadas, faltas, desmarcadas, semBaixa,
            Taxa(realizadas, faltas, desmarcadas, semBaixa),
            visiveis.Sum(l => l.TratamentosIniciados ?? 0));
    }

    /// <summary>
    /// Horários e realizados por semana (segunda a domingo), somando as unidades visíveis. Só semanas
    /// INTEIRAS dentro da janela: a primeira e a última quase sempre são pedaços e pareceriam quedas.
    /// </summary>
    internal static IReadOnlyList<SpineRedeAdesaoSemanaDto> MontarSerieSemanal(
        IEnumerable<IReadOnlyList<SpineSchedule>> unidades, DateOnly de, DateOnly ate) =>
        unidades
            .SelectMany(u => u.Where(Conta))
            .GroupBy(s => InicioDaSemana(SpineApiClient.DiaLocal(s.DateAttendance!.Value)))
            .Where(g => g.Key >= de && g.Key.AddDays(6) <= ate)
            .OrderBy(g => g.Key)
            .Select(g => new SpineRedeAdesaoSemanaDto(
                g.Key, g.Count(), g.Count(s => s.IdStatus == SpineApiClient.ScheduleStatus.Atendido)))
            .ToList();

    internal static DateOnly InicioDaSemana(DateOnly dia) =>
        dia.AddDays(-(((int)dia.DayOfWeek + 6) % 7));
}
