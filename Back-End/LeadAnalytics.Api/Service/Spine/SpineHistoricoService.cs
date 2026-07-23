using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace LeadAnalytics.Api.Service.Spine;

/// <summary>
/// Preserva a agenda do Doutor Hérnia no nosso banco, para o dashboard mostrar
/// histórico além dos 100 dias que a API deles permite consultar.
///
/// Desenho: o n8n só DISPARA (cron); esta classe puxa do Spine e faz upsert. Assim
/// o token e a conversão de fuso ficam num lugar só (a API), e o n8n nunca vê o
/// token nem reimplementa regra.
///
/// Janela móvel de 7 dias: o status de um horário muda depois (AGENDADO vira
/// ATENDIDO/DESMARCADO), então reprocessar a última semana todo dia corrige as
/// linhas já gravadas — a última captura vence, casando por (UnitId, IdSchedule).
/// </summary>
public class SpineHistoricoService(
    AppDbContext db,
    SpineApiClient client,
    SpineTokenStore tokens,
    ILogger<SpineHistoricoService> logger)
{
    private readonly AppDbContext _db = db;
    private readonly SpineApiClient _client = client;
    private readonly SpineTokenStore _tokens = tokens;
    private readonly ILogger<SpineHistoricoService> _logger = logger;

    private static readonly (int Id, string Nome)[] Categorias =
    [
        (SpineApiClient.ScheduleCategory.Avaliacao, "AVALIACAO"),
        (SpineApiClient.ScheduleCategory.Sessao, "SESSAO"),
        (SpineApiClient.ScheduleCategory.Retorno, "RETORNO"),
        (SpineApiClient.ScheduleCategory.RetornoComExames, "RETORNO C/ EXAMES"),
        (SpineApiClient.ScheduleCategory.RetornoAposTratamento, "RETORNO POS-TRATAMENTO"),
    ];

    /// <summary>Captura os últimos <paramref name="dias"/> dias e faz upsert. Retorna quantos gravou.</summary>
    public async Task<(bool Conectado, int Gravados)> SyncAsync(
        int unitId, int dias = 7, CancellationToken ct = default)
    {
        var token = await _tokens.GetTokenAsync(unitId, ct);
        if (token is null) return (false, 0);

        var ate = SpineApiClient.DiaLocal(DateTime.UtcNow);
        var de = ate.AddDays(-Math.Clamp(dias, 1, SpineApiClient.MaxDiasJanela));

        // Puxa por categoria (a agenda não devolve o campo categoria; é o único jeito
        // de saber se é avaliação/sessão/retorno) e junta numa lista só.
        var capturado = new List<(SpineSchedule Row, int IdCat, string Cat)>();
        foreach (var (idCat, nome) in Categorias)
        {
            var rows = await _client.SearchSchedulesAsync(token, de, ate, idCat, ct);
            capturado.AddRange(rows.Select(r => (r, idCat, nome)));
        }
        if (capturado.Count == 0) return (true, 0);

        // Carrega o que já existe dessas linhas para decidir insert vs update.
        var ids = capturado.Select(c => c.Row.IdSchedule).Distinct().ToList();
        var existentes = await _db.SpineScheduleSnapshots
            .Where(s => s.UnitId == unitId && ids.Contains(s.IdSchedule))
            .ToDictionaryAsync(s => s.IdSchedule, ct);

        var agora = DateTime.UtcNow;
        var vistos = new HashSet<long>();
        foreach (var (r, idCat, cat) in capturado)
        {
            // A mesma agenda pode aparecer em duas categorias por engano; fica a 1ª.
            if (!vistos.Add(r.IdSchedule)) continue;
            var utc = r.DateAttendance ?? agora;

            if (existentes.TryGetValue(r.IdSchedule, out var e))
            {
                e.IdTreatment = r.IdTreatment;
                e.DateAttendanceUtc = utc;
                e.DiaLocal = SpineApiClient.DiaLocal(utc);
                e.IdCategory = idCat;
                e.Categoria = cat;
                e.Paciente = r.ClientName?.Trim();
                e.Profissional = r.PhysicalTherapist?.Trim();
                e.IdStatus = r.IdStatus;
                e.StatusName = r.StatusName;
                e.ModifiedAtSpine = r.Modified;
                e.ModifiedBySpine = r.ModifiedBy?.Trim();
                e.CapturedAt = agora;
            }
            else
            {
                _db.SpineScheduleSnapshots.Add(new SpineScheduleSnapshot
                {
                    UnitId = unitId,
                    IdSchedule = r.IdSchedule,
                    IdTreatment = r.IdTreatment,
                    DateAttendanceUtc = utc,
                    DiaLocal = SpineApiClient.DiaLocal(utc),
                    IdCategory = idCat,
                    Categoria = cat,
                    Paciente = r.ClientName?.Trim(),
                    Profissional = r.PhysicalTherapist?.Trim(),
                    IdStatus = r.IdStatus,
                    StatusName = r.StatusName,
                    ModifiedAtSpine = r.Modified,
                    ModifiedBySpine = r.ModifiedBy?.Trim(),
                    CapturedAt = agora,
                });
            }
        }

        var gravados = await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Spine histórico: unidade {UnitId} capturou {N} linhas ({De}→{Ate})",
            unitId, vistos.Count, de, ate);
        return (true, vistos.Count);
    }
}
