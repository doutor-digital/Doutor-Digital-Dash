using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.DTOs.Saude;
using LeadAnalytics.Api.Service.Spine;
using Microsoft.EntityFrameworkCore;

namespace LeadAnalytics.Api.Service;

/// <summary>
/// O que precisa de alguém agora.
///
/// MÉTRICA DIZ O QUE ACONTECEU; FILA DIZ O QUE FAZER
/// -------------------------------------------------
/// O dashboard inteiro responde "como foi o mês". Ninguém abre isso todo dia. Fila é o que
/// muda: quatro listas curtas, com nome e telefone, que somem quando resolvidas.
///
/// CADA FILA NASCEU DE UM BURACO REAL DESTA OPERAÇÃO
/// -------------------------------------------------
/// • Lead sem primeira resposta: hoje ninguém vê um lead esquecido até ele virar perdido.
/// • Agendado sem data: 29% dos agendados. Sem data não entra em lembrete nenhum e o
///   paciente vira no-show sem ninguém perceber.
/// • Consulta de amanhã: a lista que a recepção precisa para confirmar.
/// • Faltou ontem: a Spine devolve o status real (faltou, desmarcou), então é a única fila
///   aqui que não depende de preenchimento de ninguém.
/// </summary>
public class FilasService(AppDbContext db, KpiConfigService kpiConfig)
{
    private readonly AppDbContext _db = db;
    private readonly KpiConfigService _kpiConfig = kpiConfig;

    /// <summary>Lead que entrou e ninguém tocou dentro disso vira fila.</summary>
    private const int HorasSemResposta = 2;

    private static readonly string[] EtapasAgendado =
        [LeadStages.AgendadoSemPagamento, LeadStages.AgendadoComPagamento];

    public async Task<FilasDto> GetAsync(int tenantId, int? unitId, CancellationToken ct = default)
    {
        var agora = DateTime.UtcNow;
        var hojeLocal = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTimeFromUtc(agora, SpineApiClient.BrTz));

        var escopo = _db.Leads.AsNoTracking()
            .Where(l => l.TenantId == tenantId && (!unitId.HasValue || l.UnitId == unitId.Value));

        var filas = new List<FilaDto>();

        // ── 1. Entrou e ninguém respondeu ────────────────────────────────────
        var corte = agora.AddHours(-HorasSemResposta);
        var semResposta = await escopo
            .Where(l => l.CreatedAt <= corte
                        && l.CreatedAt >= agora.AddDays(-3)
                        && l.HadInteraction != true
                        && l.CurrentStage != LeadStages.Perdido)
            .OrderBy(l => l.CreatedAt)
            .Take(50)
            .Select(l => new FilaItemDto
            {
                LeadId = l.Id,
                Nome = l.Name,
                Telefone = l.Phone,
                Quando = l.CreatedAt,
            })
            .ToListAsync(ct);

        filas.Add(new FilaDto
        {
            Id = "sem_resposta",
            Titulo = $"Entraram há mais de {HorasSemResposta}h e ninguém respondeu",
            Porque = "Lead esquecido só aparece quando já virou perdido.",
            Urgencia = "alta",
            Quantidade = semResposta.Count,
            Itens = semResposta,
        });

        // ── 2. Agendado sem data ─────────────────────────────────────────────
        var mapa = unitId.HasValue
            ? await _kpiConfig.GetLeadProfileConfigAsync(unitId.Value, ct)
            : new KpiConfigService.LeadProfileFields();

        var agendadoSemData = await escopo
            .Where(l => EtapasAgendado.Contains(l.CurrentStage!)
                        && l.AppointmentScheduledAt == null)
            .OrderByDescending(l => l.CreatedAt)
            .Take(50)
            .Select(l => new FilaItemDto
            {
                LeadId = l.Id,
                Nome = l.Name,
                Telefone = l.Phone,
                Quando = l.CreatedAt,
            })
            .ToListAsync(ct);

        filas.Add(new FilaDto
        {
            Id = "agendado_sem_data",
            Titulo = "Agendados sem data preenchida",
            Porque = "Sem data não entra em lembrete nenhum, e o paciente vira falta sem aviso.",
            Urgencia = "alta",
            Quantidade = agendadoSemData.Count,
            Itens = agendadoSemData,
        });

        // ── 3 e 4. Da agenda da franquia: amanhã e as faltas de ontem ────────
        // Único bloco aqui que não depende de ninguém preencher nada: o status vem
        // do sistema clínico.
        var amanha = hojeLocal.AddDays(1);
        var ontem = hojeLocal.AddDays(-1);

        var agendaQ = _db.SpineScheduleSnapshots.AsNoTracking()
            .Where(s => !unitId.HasValue || s.UnitId == unitId.Value);

        var deAmanha = await agendaQ
            .Where(s => s.DiaLocal == amanha
                        && (s.IdStatus == SpineApiClient.ScheduleStatus.Agendado
                            || s.IdStatus == SpineApiClient.ScheduleStatus.Confirmado))
            .OrderBy(s => s.DateAttendanceUtc)
            .Take(50)
            .Select(s => new FilaItemDto
            {
                Nome = s.Paciente,
                Detalhe = s.Categoria,
                Quando = s.DateAttendanceUtc,
            })
            .ToListAsync(ct);

        filas.Add(new FilaDto
        {
            Id = "consulta_amanha",
            Titulo = "Consultas de amanhã para confirmar",
            Porque = "Confirmação na véspera é o que mais reduz falta.",
            Urgencia = "media",
            Quantidade = deAmanha.Count,
            Itens = deAmanha,
        });

        var faltasOntem = await agendaQ
            .Where(s => s.DiaLocal == ontem
                        && s.IdStatus == SpineApiClient.ScheduleStatus.NaoCompareceu)
            .Take(50)
            .Select(s => new FilaItemDto
            {
                Nome = s.Paciente,
                Detalhe = s.Categoria,
                Quando = s.DateAttendanceUtc,
            })
            .ToListAsync(ct);

        filas.Add(new FilaDto
        {
            Id = "faltou_ontem",
            Titulo = "Faltaram ontem — remarcar",
            Porque = "Quem faltou e não é procurado no dia seguinte raramente volta.",
            Urgencia = "media",
            Quantidade = faltasOntem.Count,
            Itens = faltasOntem,
        });

        return new FilasDto
        {
            TotalPendente = filas.Sum(f => f.Quantidade),
            // Fila vazia não vira linha na tela: lista de zeros ensina a ignorar o bloco.
            Filas = [.. filas.Where(f => f.Quantidade > 0)
                             .OrderByDescending(f => f.Urgencia == "alta")
                             .ThenByDescending(f => f.Quantidade)],
        };
    }
}
