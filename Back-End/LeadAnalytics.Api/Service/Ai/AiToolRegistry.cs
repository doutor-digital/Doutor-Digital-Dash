using System.Text.Json;
using LeadAnalytics.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace LeadAnalytics.Api.Service.Ai;

/// <summary>
/// Catálogo de tools (function-calling) que o gpt-4o-mini pode invocar durante
/// uma conversa. Cada tool tem:
///  - JSON schema (manda pra OpenAI no array `tools`)
///  - executor C# (roda quando o modelo invoca)
///
/// Tools READ-ONLY rodam direto. Tools WRITE retornam um "pending action" pra
/// confirmação do usuário antes de aplicar — a confirmação é feita por
/// endpoint separado /api/ai/confirm-action/{id} (não nesta classe).
/// </summary>
public class AiToolRegistry(AppDbContext db, KpiConfigService kpiService, ILogger<AiToolRegistry> logger)
{
    public record ToolDefinition(string Name, string Description, JsonElement Schema, bool IsWrite);

    private List<ToolDefinition>? _cached;

    public IReadOnlyList<ToolDefinition> All()
    {
        if (_cached != null) return _cached;

        _cached = new List<ToolDefinition>
        {
            ReadTool(
                "get_unit_kpis",
                "Retorna os KPIs principais de uma unidade no período: total de leads, distribuição por etapa do funil, top atendentes e top origens.",
                """{"type":"object","properties":{"unit_id":{"type":"integer","description":"id da unidade"},"days":{"type":"integer","description":"janela em dias contando do hoje (ex.: 7, 30, 90)","default":30}},"required":["unit_id"]}"""),

            ReadTool(
                "list_recent_leads",
                "Lista os leads mais recentemente atualizados de uma unidade. Útil quando o usuário pergunta 'quais leads chegaram hoje' ou 'quem é o último'.",
                """{"type":"object","properties":{"unit_id":{"type":"integer"},"limit":{"type":"integer","description":"quantos leads retornar (default 10, max 30)","default":10},"stage":{"type":"string","description":"filtrar por etapa do funil (ex.: AGENDOU). Opcional."}},"required":["unit_id"]}"""),

            ReadTool(
                "get_custom_field_top",
                "Top valores de um campo customizado da Kommo (ex.: Origem, Tratamento Indicado, Profissão, Motivo do Não Agendamento, Sexo).",
                """{"type":"object","properties":{"unit_id":{"type":"integer"},"field_name":{"type":"string","description":"nome do campo, ex.: 'Origem', 'Profissão', 'Tratamento Indicado'"},"days":{"type":"integer","default":30},"limit":{"type":"integer","default":10}},"required":["unit_id","field_name"]}"""),

            ReadTool(
                "get_sexo_outcome",
                "Distribuição de Sexo × Desfecho na unidade no período (agendou/compareceu/fechou/faltou). Responde 'qual sexo mais agenda' etc.",
                """{"type":"object","properties":{"unit_id":{"type":"integer"},"days":{"type":"integer","default":30}},"required":["unit_id"]}"""),

            ReadTool(
                "search_leads",
                "Busca leads pelo nome ou telefone. Útil quando o usuário menciona 'o lead Maria' e a IA precisa saber qual.",
                """{"type":"object","properties":{"unit_id":{"type":"integer"},"query":{"type":"string","description":"nome ou telefone (pode ser parcial)"}},"required":["unit_id","query"]}"""),
        };

        return _cached;
    }

    private static ToolDefinition ReadTool(string name, string description, string schemaJson)
    {
        using var doc = JsonDocument.Parse(schemaJson);
        return new ToolDefinition(name, description, doc.RootElement.Clone(), IsWrite: false);
    }

    /// <summary>
    /// Executa a tool e devolve o resultado como JSON serializado. Chamado pelo
    /// loop de function-calling. <paramref name="tenantId"/> e <paramref name="unitIdContext"/>
    /// vêm do controller; a tool não confia no que o LLM passou no unit_id sem validar.
    /// </summary>
    public async Task<string> ExecuteAsync(
        string toolName, JsonElement args,
        int tenantId, int? unitIdContext, CancellationToken ct)
    {
        var unitId = GetInt(args, "unit_id") ?? unitIdContext;
        if (unitId is null) return Json(new { error = "unit_id é obrigatório" });

        // Valida que a unidade pertence ao tenant
        var clinicId = await db.Units.AsNoTracking()
            .Where(u => u.Id == unitId.Value).Select(u => (int?)u.ClinicId).FirstOrDefaultAsync(ct);
        if (clinicId != tenantId)
            return Json(new { error = "unidade não pertence ao seu tenant" });

        var days = GetInt(args, "days") ?? 30;
        // Janela de dia BRT (UTC-3) convertida pra UTC: hoje 00:00 BRT = 03:00 UTC.
        // Garante que o "total de leads" do tool case com o que o usuário enxerga
        // no relógio dele, não com a meia-noite UTC.
        var brOffset = TimeSpan.FromHours(3);
        var nowBr = DateTime.UtcNow.Add(-brOffset);
        var dayTo = nowBr.Date;
        var to = DateTime.SpecifyKind(dayTo.AddDays(1).AddTicks(-1) + brOffset, DateTimeKind.Utc);
        var from = DateTime.SpecifyKind(dayTo.AddDays(-days + 1) + brOffset, DateTimeKind.Utc);

        try
        {
            switch (toolName)
            {
                case "get_unit_kpis": return await GetUnitKpisAsync(tenantId, unitId.Value, from, to, ct);
                case "list_recent_leads":
                    return await ListRecentLeadsAsync(
                        tenantId, unitId.Value,
                        Math.Min(GetInt(args, "limit") ?? 10, 30),
                        GetString(args, "stage"), ct);
                case "get_custom_field_top":
                    return await GetCustomFieldTopAsync(
                        tenantId, unitId.Value,
                        GetString(args, "field_name") ?? "",
                        from, to,
                        Math.Min(GetInt(args, "limit") ?? 10, 30), ct);
                case "get_sexo_outcome":
                    return await GetSexoOutcomeAsync(tenantId, unitId.Value, from, to, ct);
                case "search_leads":
                    return await SearchLeadsAsync(tenantId, unitId.Value, GetString(args, "query") ?? "", ct);
                default:
                    return Json(new { error = $"tool desconhecida: {toolName}" });
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[ai-tool] {Tool} falhou", toolName);
            return Json(new { error = ex.Message });
        }
    }

    // ─── Tool implementations ──────────────────────────────────────────────

    private async Task<string> GetUnitKpisAsync(int tenantId, int unitId, DateTime from, DateTime to, CancellationToken ct)
    {
        var q = db.Leads.AsNoTracking()
            .Where(l => l.TenantId == tenantId && l.UnitId == unitId
                        && l.CreatedAt >= from && l.CreatedAt <= to);

        var total = await q.CountAsync(ct);
        var byStage = await q.GroupBy(l => l.CurrentStage)
            .Select(g => new { stage = g.Key, count = g.Count() })
            .OrderByDescending(x => x.count).Take(8).ToListAsync(ct);
        var topAtt = await q.Where(l => l.AttendantId != null)
            .GroupBy(l => l.AttendantId)
            .Select(g => new { att = g.Key, count = g.Count() })
            .OrderByDescending(x => x.count).Take(5).ToListAsync(ct);
        var topSrc = await q.GroupBy(l => l.Source)
            .Select(g => new { src = g.Key, count = g.Count() })
            .OrderByDescending(x => x.count).Take(5).ToListAsync(ct);

        return Json(new { total_leads = total, by_stage = byStage, top_responsaveis = topAtt, top_origens = topSrc, from, to });
    }

    private async Task<string> ListRecentLeadsAsync(int tenantId, int unitId, int limit, string? stage, CancellationToken ct)
    {
        var q = db.Leads.AsNoTracking()
            .Where(l => l.TenantId == tenantId && l.UnitId == unitId);
        if (!string.IsNullOrWhiteSpace(stage)) q = q.Where(l => l.CurrentStage == stage);

        var leads = await q.OrderByDescending(l => l.UpdatedAt)
            .Take(limit)
            .Select(l => new
            {
                id = l.Id,
                name = l.Name,
                phone = l.Phone,
                stage = l.CurrentStage,
                source = l.Source,
                campaign = l.Campaign,
                updated_at = l.UpdatedAt,
            })
            .ToListAsync(ct);
        return Json(new { count = leads.Count, leads });
    }

    private async Task<string> GetCustomFieldTopAsync(int tenantId, int unitId, string fieldName, DateTime from, DateTime to, int limit, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(fieldName))
            return Json(new { error = "field_name é obrigatório" });

        var cross = await kpiService.CustomFieldsCrossAnalysisAsync(tenantId, unitId, from, to, limit, ct);
        var n = fieldName.ToLowerInvariant();

        var data = n switch
        {
            var s when s.Contains("origem") => cross.Origem.Take(limit).Cast<object>().ToList(),
            var s when s.Contains("tratamento") && s.Contains("indicad") => cross.TratamentoIndicado.Take(limit).Cast<object>().ToList(),
            var s when s.Contains("tratamento") && s.Contains("fechad") => cross.TratamentoFechado.Take(limit).Cast<object>().ToList(),
            var s when s.Contains("motivo") => cross.MotivoNaoAgendamento.Take(limit).Cast<object>().ToList(),
            var s when s.Contains("profiss") => cross.Profissao.Take(limit).Cast<object>().ToList(),
            var s when s.Contains("qualifica") => cross.Qualificacao.Take(limit).Cast<object>().ToList(),
            var s when s.Contains("respons") => cross.ResponsavelAgendamento.Take(limit).Cast<object>().ToList(),
            _ => new List<object>(),
        };
        return Json(new { field_name = fieldName, total_in_sample = data.Count, top = data });
    }

    private async Task<string> GetSexoOutcomeAsync(int tenantId, int unitId, DateTime from, DateTime to, CancellationToken ct)
    {
        var cross = await kpiService.CustomFieldsCrossAnalysisAsync(tenantId, unitId, from, to, 12, ct);
        return Json(new { sexo_outcomes = cross.SexoByOutcome });
    }

    private async Task<string> SearchLeadsAsync(int tenantId, int unitId, string query, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query))
            return Json(new { error = "query é obrigatório" });

        var q = query.Trim().ToLowerInvariant();
        var leads = await db.Leads.AsNoTracking()
            .Where(l => l.TenantId == tenantId && l.UnitId == unitId
                        && (l.Name.ToLower().Contains(q) || (l.Phone != null && l.Phone.Contains(q))))
            .OrderByDescending(l => l.UpdatedAt)
            .Take(10)
            .Select(l => new
            {
                id = l.Id,
                name = l.Name,
                phone = l.Phone,
                stage = l.CurrentStage,
                source = l.Source,
            })
            .ToListAsync(ct);
        return Json(new { query, count = leads.Count, leads });
    }

    // ─── Helpers ───────────────────────────────────────────────────────────

    private static int? GetInt(JsonElement el, string key) =>
        el.ValueKind == JsonValueKind.Object && el.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number
            ? v.GetInt32() : null;

    private static string? GetString(JsonElement el, string key) =>
        el.ValueKind == JsonValueKind.Object && el.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() : null;

    private static string Json(object o) =>
        JsonSerializer.Serialize(o, new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
}
