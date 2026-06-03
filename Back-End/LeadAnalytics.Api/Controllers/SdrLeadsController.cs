using System.Globalization;
using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.DTOs.Sdr;
using LeadAnalytics.Api.Service;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LeadAnalytics.Api.Controllers;

/// <summary>
/// Endpoints para a tela "Revisar leads" do SDR.
///
/// Modelo de dados: a Cloudia empurra leads via POST /webhooks/cloudia →
/// LeadService.SaveLeadAsync persiste na tabela `leads`. Esta controller
/// expõe esses leads no formato esperado pelo front (SdrLeadResponseDto)
/// pra mergeagem com o store local de revisão, com filtros de unidade e
/// janela de horário (turnos) para que cada secretária só puxe os leads
/// que entraram no SEU turno na sua unidade.
/// </summary>
[ApiController]
[Route("api/sdr/leads")]
[Authorize]
public class SdrLeadsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly TenantUnitGuard _tenantGuard;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<SdrLeadsController> _logger;

    public SdrLeadsController(
        AppDbContext db,
        TenantUnitGuard tenantGuard,
        ICurrentUser currentUser,
        ILogger<SdrLeadsController> logger)
    {
        _db = db;
        _tenantGuard = tenantGuard;
        _currentUser = currentUser;
        _logger = logger;
    }

    /// <summary>
    /// POST /api/sdr/leads/sync-from-cloudia
    /// Lê leads gravados pelo webhook Cloudia, filtrando por unidade,
    /// data e janela de horário. Devolve no formato SdrLeadResponseDto
    /// para o front mergiar no localStorage.
    ///
    /// Body (todos opcionais):
    /// {
    ///   "unitId": 8020,                  // obrigatório se NÃO for super_admin sem unitId no JWT
    ///   "from": "2026-05-01T00:00:00Z",  // default: últimos 30 dias
    ///   "to":   "2026-05-09T23:59:59Z",  // default: agora
    ///   "shift": "morning|overnight|custom|null",
    ///   "timeStart": "08:00",            // HH:mm em horário local de Brasília
    ///   "timeEnd":   "12:00",            // se timeEnd &lt; timeStart, atravessa meia-noite
    ///   "limit": 500
    /// }
    /// </summary>
    [HttpPost("sync-from-leads")]
    public async Task<IActionResult> SyncFromLeads(
        [FromBody] SdrSyncRequestDto? body,
        CancellationToken ct)
    {
        body ??= new SdrSyncRequestDto();

        // Resolve tenant + unit. Quem não é super_admin precisa estar no tenant.
        var (error, tenantId) = await _tenantGuard.ResolveTenantAsync(body.UnitId, ct);
        if (error is not null) return error;

        // Janela padrão: últimos 30 dias.
        var to = body.To ?? DateTime.UtcNow;
        var from = body.From ?? to.AddDays(-30);
        if (from > to) (from, to) = (to, from);

        // Aplica preset de turno se vier.
        var (shiftStart, shiftEnd) = ResolveShiftWindow(body);

        // Limita pra não estourar memória/payload.
        var limit = body.Limit is > 0 and <= 2000 ? body.Limit.Value : 500;

        var query = _db.Leads
            .AsNoTracking()
            .Include(l => l.Unit)
            .Include(l => l.Attendant)
            .Where(l => l.CreatedAt >= from && l.CreatedAt <= to)
            .AsQueryable();

        if (tenantId.HasValue)
            query = query.Where(l => l.TenantId == tenantId.Value);

        // Filtro de unidade (quando o caller passou explicitamente).
        if (body.UnitId.HasValue)
            query = query.Where(l => l.UnitId == body.UnitId.Value);

        var leads = await query
            .OrderByDescending(l => l.CreatedAt)
            .Take(limit * 2)  // pega mais pra compensar o filtro de turno
            .ToListAsync(ct);

        // Filtro de janela de horário (em memória — usa horário local Brasília).
        // Ex.: shiftStart=20:00, shiftEnd=07:50 → atravessa meia-noite.
        if (shiftStart.HasValue && shiftEnd.HasValue)
        {
            var brasilia = GetBrasiliaTz();
            leads = leads.Where(l =>
                IsInsideShift(
                    TimeZoneInfo.ConvertTimeFromUtc(
                        DateTime.SpecifyKind(l.CreatedAt, DateTimeKind.Utc),
                        brasilia
                    ),
                    shiftStart.Value,
                    shiftEnd.Value)
            ).ToList();
        }

        // Aplica o limit final.
        if (leads.Count > limit) leads = leads.Take(limit).ToList();

        var items = leads.Select(MapToSdrDto).ToList();

        _logger.LogInformation(
            "🔄 SDR sync: tenant={Tenant} unit={Unit} window=[{From:o},{To:o}] shift={ShiftStart}-{ShiftEnd} → {Count} leads",
            tenantId, body.UnitId, from, to, shiftStart, shiftEnd, items.Count);

        return Ok(new SdrSyncSummaryDto
        {
            Created = items.Count,
            Skipped = 0,
            Updated = 0,
            Failed = 0,
            Items = items,
            From = from,
            To = to,
            UnitId = body.UnitId,
            ShiftStart = shiftStart?.ToString(@"hh\:mm"),
            ShiftEnd = shiftEnd?.ToString(@"hh\:mm"),
        });
    }

    /// <summary>
    /// Mapeia o preset / par HH:mm para um intervalo TimeSpan.
    /// Presets:
    ///  - morning   : 08:00 - 12:00 (turno manhã)
    ///  - overnight : 20:00 - 07:50 (atravessa meia-noite)
    ///  - custom    : usa timeStart/timeEnd
    ///  - null      : sem filtro de horário
    /// </summary>
    private static (TimeSpan? Start, TimeSpan? End) ResolveShiftWindow(SdrSyncRequestDto body)
    {
        switch ((body.Shift ?? "").Trim().ToLowerInvariant())
        {
            case "morning":
                return (new TimeSpan(8, 0, 0), new TimeSpan(12, 0, 0));
            case "overnight":
                return (new TimeSpan(20, 0, 0), new TimeSpan(7, 50, 0));
            case "custom":
            case "":
            case null:
                if (TryParseTime(body.TimeStart, out var s) &&
                    TryParseTime(body.TimeEnd, out var e))
                    return (s, e);
                return (null, null);
            default:
                return (null, null);
        }
    }

    private static bool TryParseTime(string? value, out TimeSpan ts)
    {
        ts = default;
        if (string.IsNullOrWhiteSpace(value)) return false;
        // aceita "HH:mm" ou "HH:mm:ss"
        return TimeSpan.TryParseExact(value, new[] { @"hh\:mm", @"hh\:mm\:ss" },
            CultureInfo.InvariantCulture, out ts);
    }

    /// <summary>
    /// Retorna true se 'when' (já em horário Brasília) cair dentro da janela
    /// [start, end). Se end &lt; start, a janela atravessa meia-noite (ex.: 20:00 → 07:50).
    /// </summary>
    private static bool IsInsideShift(DateTime when, TimeSpan start, TimeSpan end)
    {
        var t = when.TimeOfDay;
        if (start <= end) return t >= start && t < end;
        // wrap: 20:00..23:59:59 OU 00:00..07:50
        return t >= start || t < end;
    }

    private static TimeZoneInfo GetBrasiliaTz()
    {
        // Tenta primeiro o nome Linux (Railway/Docker), depois o Windows.
        try { return TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo"); }
        catch { /* fallback abaixo */ }
        try { return TimeZoneInfo.FindSystemTimeZoneById("E. South America Standard Time"); }
        catch { /* último fallback */ }
        // Sem TZ no SO → UTC-3 fixo.
        return TimeZoneInfo.CreateCustomTimeZone("BRT", TimeSpan.FromHours(-3), "BRT", "BRT");
    }

    private static SdrLeadResponseDto MapToSdrDto(Models.Lead l)
    {
        var cf = new List<string>();
        if (!string.IsNullOrWhiteSpace(l.Name))         cf.Add("nome");
        if (!string.IsNullOrWhiteSpace(l.Phone))        cf.Add("telefone");
        if (!string.IsNullOrWhiteSpace(l.Source) && l.Source != "DESCONHECIDO") cf.Add("origem");
        if (!string.IsNullOrWhiteSpace(l.Observations)) cf.Add("observacao");
        if (l.HasAppointment)                            cf.Add("agendouConsulta");
        if (!string.IsNullOrWhiteSpace(l.CurrentStage)) cf.Add("situacao");
        cf.Add("dataOrigem");
        if (l.UpdatedAt > l.CreatedAt) cf.Add("dataModificacao");

        return new SdrLeadResponseDto
        {
            Id = l.Id,
            TenantId = l.TenantId,
            ExternalId = l.ExternalId,
            Nome = l.Name ?? string.Empty,
            Telefone = l.Phone ?? string.Empty,
            Tipo = "Cadastro",
            Origem = l.Source ?? "Cloudia",
            Interacao = false,
            AgendouConsulta = l.HasAppointment,
            DataAgendamento = null,
            NomeResponsavel = l.Attendant?.Name ?? string.Empty,
            Observacao = l.Observations,
            Situacao = l.CurrentStage,
            Clinica = l.Unit?.Name,
            DataOrigem = l.CreatedAt.ToString("o"),
            DataModificacao = l.UpdatedAt.ToString("o"),
            Source = "cloudia",
            Status = "pendente_revisao",
            CloudiaFields = cf,
            CloudiaReceivedAt = l.CreatedAt.ToString("o"),
            CloudiaWebhookEvent = "CUSTOMER_CREATED",
            UnitId = l.UnitId,
            AttendantId = l.AttendantId,
            CreatedAt = l.CreatedAt.ToString("o"),
            UpdatedAt = l.UpdatedAt.ToString("o"),
            CustomFields = ParseCustomFields(l.CustomFieldsJson),
        };
    }

    /// <summary>
    /// Lê o <c>CustomFieldsJson</c> do lead (array <c>[{field_id,field_name,field_code,type,value}]</c>)
    /// e devolve a lista legível pra o SDR ver todos os campos da Kommo na revisão.
    /// </summary>
    private static List<DTOs.Sdr.SdrCustomFieldDto> ParseCustomFields(string? json)
    {
        var list = new List<DTOs.Sdr.SdrCustomFieldDto>();
        if (string.IsNullOrWhiteSpace(json)) return list;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Array) return list;
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                if (el.ValueKind != System.Text.Json.JsonValueKind.Object) continue;
                var name = el.TryGetProperty("field_name", out var n)
                    && n.ValueKind == System.Text.Json.JsonValueKind.String ? n.GetString() : null;
                if (string.IsNullOrWhiteSpace(name)) continue;

                string? value = null;
                if (el.TryGetProperty("value", out var v))
                {
                    value = v.ValueKind switch
                    {
                        System.Text.Json.JsonValueKind.String => v.GetString(),
                        System.Text.Json.JsonValueKind.Number => v.GetRawText(),
                        System.Text.Json.JsonValueKind.True => "Sim",
                        System.Text.Json.JsonValueKind.False => "Não",
                        _ => null,
                    };
                }

                list.Add(new DTOs.Sdr.SdrCustomFieldDto
                {
                    FieldId = el.TryGetProperty("field_id", out var fid) && fid.TryGetInt64(out var idv) ? idv : 0,
                    FieldName = name!,
                    FieldCode = el.TryGetProperty("field_code", out var fc)
                        && fc.ValueKind == System.Text.Json.JsonValueKind.String ? fc.GetString() : null,
                    Type = el.TryGetProperty("type", out var t)
                        && t.ValueKind == System.Text.Json.JsonValueKind.String ? t.GetString() : null,
                    Value = value,
                });
            }
        }
        catch (System.Text.Json.JsonException) { /* json malformado — ignora */ }
        return list;
    }
}
