using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.DTOs.Saude;
using LeadAnalytics.Api.Service.Spine;
using Microsoft.EntityFrameworkCore;

namespace LeadAnalytics.Api.Service;

/// <summary>
/// Falta na agenda da clínica, com o desfecho inteiro em volta.
///
/// O CARD ANTIGO ESTAVA CERTO, E ERA ISSO O PROBLEMA
/// --------------------------------------------------
/// Ele mostrava "1" e parecia quebrado. Medido em Imperatriz, 30 dias: existe exatamente UM
/// agendamento marcado como "Não compareceu" — contra 48 marcados como "Desmarcado". A
/// recepção usa Desmarcado para tudo: falta, cancelamento do paciente e cancelamento da
/// clínica caem no mesmo balde.
///
/// Um número tecnicamente correto que ninguém consegue interpretar é pior que um erro: erro
/// alguém corrige. Por isso este serviço devolve o desfecho completo — agendado, compareceu,
/// faltou, desmarcado, remarcado — e ACUSA quando o balde guarda-chuva está mascarando falta.
///
/// POR QUE NÃO CHUTAMOS QUE DESMARCADO É FALTA
/// -------------------------------------------
/// Seria fácil somar desmarcado ao no-show e mostrar 49. Mas desmarcar na véspera e não
/// aparecer no dia são coisas diferentes: uma a clínica consegue preencher com outro paciente,
/// a outra é hora perdida. Somar as duas inventaria um número que a clínica não sabe defender.
/// </summary>
public class NoShowService(AppDbContext db)
{
    private readonly AppDbContext _db = db;

    /// <summary>
    /// A partir de quantos desmarcados por falta registrada o balde vira suspeito.
    /// Toda clínica tem alguma falta; nenhuma tem trinta cancelamentos para cada uma.
    /// </summary>
    private const int RazaoSuspeita = 5;

    public async Task<NoShowDto> GetAsync(
        int tenantId, int? unitId, DateOnly de, DateOnly ate, CancellationToken ct = default)
    {
        var atual = await MedirAsync(unitId, de, ate, ct);

        // Período anterior de mesmo tamanho, colado no início deste. Comparar mês cheio com
        // meia semana produziria uma variação que não quer dizer nada.
        var dias = ate.DayNumber - de.DayNumber + 1;
        var anteriorAte = de.AddDays(-1);
        var anteriorDe = anteriorAte.AddDays(-(dias - 1));
        var anterior = await MedirAsync(unitId, anteriorDe, anteriorAte, ct);

        var faltas = await _db.SpineScheduleSnapshots.AsNoTracking()
            .Where(s => (!unitId.HasValue || s.UnitId == unitId.Value)
                        && s.DiaLocal >= de && s.DiaLocal <= ate
                        && s.IdStatus == SpineApiClient.ScheduleStatus.NaoCompareceu)
            .OrderByDescending(s => s.DateAttendanceUtc)
            .Take(200)
            .Select(s => new NoShowItemDto
            {
                Paciente = s.Paciente,
                Profissional = s.Profissional,
                Categoria = s.Categoria,
                Quando = s.DateAttendanceUtc,
                Status = s.StatusName,
            })
            .ToListAsync(ct);

        // Desmarcados entram na lista MARCADOS como tal: quem for conferir precisa ver os dois
        // grupos lado a lado para decidir se o balde está sendo usado como falta.
        var desmarcados = await _db.SpineScheduleSnapshots.AsNoTracking()
            .Where(s => (!unitId.HasValue || s.UnitId == unitId.Value)
                        && s.DiaLocal >= de && s.DiaLocal <= ate
                        && s.IdStatus == SpineApiClient.ScheduleStatus.Desmarcado)
            .OrderByDescending(s => s.DateAttendanceUtc)
            .Take(200)
            .Select(s => new NoShowItemDto
            {
                Paciente = s.Paciente,
                Profissional = s.Profissional,
                Categoria = s.Categoria,
                Quando = s.DateAttendanceUtc,
                Status = s.StatusName,
            })
            .ToListAsync(ct);

        var suspeito = atual.Faltou > 0
            ? atual.Desmarcado / atual.Faltou >= RazaoSuspeita
            : atual.Desmarcado >= RazaoSuspeita;

        return new NoShowDto
        {
            De = de,
            Ate = ate,
            Agendados = atual.Total,
            Compareceram = atual.Compareceu,
            Faltaram = atual.Faltou,
            Desmarcados = atual.Desmarcado,
            Remarcados = atual.Remarcado,
            AindaPorVir = atual.PorVir,

            // Denominador é o que JÁ ACONTECEU: consulta de amanhã não é acerto nem falha.
            Resolvidos = atual.Resolvidos,
            PercentualFalta = atual.Resolvidos == 0
                ? 0 : Math.Round(100.0 * atual.Faltou / atual.Resolvidos, 1),
            PercentualComparecimento = atual.Resolvidos == 0
                ? 0 : Math.Round(100.0 * atual.Compareceu / atual.Resolvidos, 1),

            AnteriorFaltaram = anterior.Faltou,
            AnteriorAgendados = anterior.Total,
            AnteriorPercentualFalta = anterior.Resolvidos == 0
                ? 0 : Math.Round(100.0 * anterior.Faltou / anterior.Resolvidos, 1),
            TemAnterior = anterior.Total > 0,

            BaldeSuspeito = suspeito,
            AvisoBalde = suspeito
                ? $"{atual.Desmarcado} desmarcados contra {atual.Faltou} falta(s) registrada(s). "
                + "A recepção provavelmente marca falta como Desmarcado — enquanto isso não mudar, "
                + "o número de falta é menor que a realidade."
                : null,

            Faltas = faltas,
            Desmarcadas = desmarcados,
        };
    }

    private async Task<Medida> MedirAsync(int? unitId, DateOnly de, DateOnly ate, CancellationToken ct)
    {
        var linhas = await _db.SpineScheduleSnapshots.AsNoTracking()
            .Where(s => (!unitId.HasValue || s.UnitId == unitId.Value)
                        && s.DiaLocal >= de && s.DiaLocal <= ate)
            .Select(s => s.IdStatus)
            .ToListAsync(ct);

        var m = new Medida
        {
            Total = linhas.Count,
            Compareceu = linhas.Count(x => x == SpineApiClient.ScheduleStatus.Atendido),
            Faltou = linhas.Count(x => x == SpineApiClient.ScheduleStatus.NaoCompareceu),
            Desmarcado = linhas.Count(x => x == SpineApiClient.ScheduleStatus.Desmarcado),
            Remarcado = linhas.Count(x => x == SpineApiClient.ScheduleStatus.Remarcado),
            PorVir = linhas.Count(x => x == SpineApiClient.ScheduleStatus.Agendado
                                       || x == SpineApiClient.ScheduleStatus.Confirmado),
        };
        m.Resolvidos = m.Total - m.PorVir;
        return m;
    }

    private sealed class Medida
    {
        public int Total, Compareceu, Faltou, Desmarcado, Remarcado, PorVir, Resolvidos;
    }
}
