using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.DTOs.Saude;
using LeadAnalytics.Api.Service.Spine;
using Microsoft.EntityFrameworkCore;

namespace LeadAnalytics.Api.Service;

/// <summary>
/// O que acontece na clínica hoje, e se a Kommo concorda com isso.
///
/// POR QUE OS DOIS JUNTOS
/// ----------------------
/// Em 05/08 a tela da franquia mostrava 4 avaliações e o relatório da Kommo mostrava 0. Os
/// dois estavam certos: são perguntas diferentes — a franquia conta consulta que ACONTECE
/// no dia, a Kommo conta lead que ENTROU no dia e agendou. Um paciente agendado dia 01 para
/// vir dia 05 aparece só do lado da franquia.
///
/// Mas a divergência só apareceu porque alguém abriu os dois sistemas e comparou na mão.
/// Divergência entre CRM comercial e sistema clínico é o erro mais caro que existe aqui, e é
/// invisível enquanto ninguém faz isso. Aqui os dois números ficam lado a lado.
///
/// A AGENDA SAI DO SNAPSHOT, NÃO DA API AO VIVO
/// --------------------------------------------
/// Já capturamos a agenda da franquia no banco. Bater na API a cada carregamento de
/// dashboard gastaria a cota deles para responder o que já temos.
/// </summary>
public class AgendaDoDiaService(AppDbContext db)
{
    private readonly AppDbContext _db = db;

    public async Task<AgendaDoDiaDto> GetAsync(
        int tenantId, int? unitId, DateOnly dia, CancellationToken ct = default)
    {
        var agenda = await _db.SpineScheduleSnapshots.AsNoTracking()
            .Where(s => (!unitId.HasValue || s.UnitId == unitId.Value) && s.DiaLocal == dia)
            .Select(s => new { s.IdCategory, s.Categoria, s.IdStatus, s.Paciente, s.DateAttendanceUtc })
            .ToListAsync(ct);

        var porCategoria = agenda
            .GroupBy(a => new { a.IdCategory, Nome = a.Categoria ?? Rotulo(a.IdCategory) })
            .Select(g => new AgendaCategoriaDto
            {
                Categoria = g.Key.Nome,
                Quantidade = g.Count(),
                Compareceram = g.Count(x => x.IdStatus == SpineApiClient.ScheduleStatus.Atendido),
                Faltaram = g.Count(x => x.IdStatus == SpineApiClient.ScheduleStatus.NaoCompareceu),
                Pendentes = g.Count(x => x.IdStatus == SpineApiClient.ScheduleStatus.Agendado
                                      || x.IdStatus == SpineApiClient.ScheduleStatus.Confirmado),
            })
            .OrderByDescending(c => c.Quantidade)
            .ToList();

        // ── O que a Kommo diz do mesmo dia ───────────────────────────────────
        // Leads que entraram no dia e agendaram. É outra pergunta, e é justamente
        // por isso que os dois números precisam aparecer juntos, com o rótulo de
        // cada um — senão a diferença vira desconfiança do sistema.
        var inicio = dia.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var deUtc = DateTime.SpecifyKind(inicio.AddHours(3), DateTimeKind.Utc);
        var ateUtc = deUtc.AddDays(1);

        var agendadosKommo = await _db.Leads.AsNoTracking()
            .CountAsync(l => l.TenantId == tenantId
                             && (!unitId.HasValue || l.UnitId == unitId.Value)
                             && l.CreatedAt >= deUtc && l.CreatedAt < ateUtc
                             && (l.HasAppointment || l.AppointmentScheduledAt != null), ct);

        var avaliacoes = porCategoria
            .Where(c => c.Categoria.Contains("AVALIA", StringComparison.OrdinalIgnoreCase))
            .Sum(c => c.Quantidade);

        return new AgendaDoDiaDto
        {
            Dia = dia,
            TotalNaClinica = agenda.Count,
            PorCategoria = porCategoria,
            AvaliacoesFranquia = avaliacoes,
            AgendadosKommo = agendadosKommo,
            Nota = agenda.Count == 0
                ? "Sem agenda capturada para este dia."
                : "A franquia conta consulta que acontece hoje; a Kommo conta lead que entrou "
                  + "hoje e agendou. Números diferentes são esperados — o que vale acompanhar "
                  + "é a diferença crescer sem explicação.",
        };
    }

    private static string Rotulo(int idCategory) => idCategory switch
    {
        SpineApiClient.ScheduleCategory.Avaliacao => "AVALIAÇÃO",
        SpineApiClient.ScheduleCategory.Sessao => "SESSÃO",
        SpineApiClient.ScheduleCategory.Retorno => "RETORNO",
        SpineApiClient.ScheduleCategory.RetornoComExames => "RETORNO COM EXAMES",
        SpineApiClient.ScheduleCategory.RetornoAposTratamento => "RETORNO APÓS TRATAMENTO",
        _ => "OUTRO",
    };
}
