using System.Globalization;
using System.Text;
using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace LeadAnalytics.Api.Service.Ai;

/// <summary>
/// Junta os indicadores da unidade no período pedido (total/agendou/compareceu/
/// fechou, top atendente, top campanha, distribuição horária, sexo × desfecho)
/// num bloco de texto-fato e despacha pro GPT-4o-mini com um prompt focado em
/// recomendações práticas pra clínica. Resposta cai pronta pro front renderizar.
/// </summary>
public class AiAnalyticsService(
    AppDbContext db,
    KpiConfigService kpiService,
    OpenAiClient openAi,
    AiKeyStorage keys,
    UnitEntryStageConfig entryStageConfig,
    ILogger<AiAnalyticsService> logger)
{
    private const string SystemPrompt =
        "Você é uma analista sênior de clínica médica que entende profundamente de marketing e vendas. " +
        "Você analisa dados operacionais de uma unidade da rede e devolve um relatório em MARKDOWN com:\n\n" +
        "1. **Resumo executivo** — 2-3 frases destacando o que mais importa no período. Inclua a variação " +
        "vs período anterior se for relevante.\n" +
        "2. **Conversão & perdas** — onde os leads estão escapando, com números. Use os motivos do não " +
        "agendamento (campos customizados) pra explicar PORQUÊ.\n" +
        "3. **Quem está bombando** — atendentes que mais agendaram/fecharam, canais, dias da semana, " +
        "horários de pico. Nomes próprios, números absolutos e % do total.\n" +
        "4. **Perfil do paciente** — sexo (com taxa de conversão por sexo!), profissões mais comuns, " +
        "tratamentos mais procurados (Tratamento Indicado) vs efetivamente contratados (Tratamento Fechado), " +
        "qualificação dos leads (Quente/Morno/Frio).\n" +
        "5. **Insights dos campos customizados** — destaque o que os campos da Kommo revelam: motivos de " +
        "não agendamento mais comuns, padrões na qualificação, tendências por responsável de agendamento.\n" +
        "6. **Recomendações práticas** — 3 a 5 ações ESPECÍFICAS que a equipe deveria tomar essa semana, " +
        "baseadas só nos dados (citar nomes, canais, horários, campos). Sem genéricos como 'melhorar o " +
        "atendimento'. Exemplo bom: 'Treinar a Ana Paula em objeções financeiras — ela tem 80 leads " +
        "marcados como Motivo: 'preço' no período'.\n\n" +
        "Seja DIRETA, em pt-BR, com números e nomes próprios extraídos dos dados. Nunca invente nomes ou " +
        "valores que não estão nos dados. Se um dado estiver vazio, diga isso explicitamente.";

    public async Task<string> AnalyzeUnitAsync(
        int tenantId, int? unitId, DateTime from, DateTime to, CancellationToken ct)
    {
        // Postgres `timestamp with time zone` exige Kind=Utc. As datas que
        // chegam do controller (vindas de "2026-06-05" no JSON) são Unspecified.
        from = AsUtc(from); to = AsUtc(to);
        var apiKey = await keys.GetAsync(tenantId, ct);
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("Chave da OpenAI não configurada para esta clínica.");

        // unitId nulo = "Todas as unidades": agrega o tenant inteiro.
        Unit? unit = null;
        if (unitId is int uid)
        {
            unit = await db.Units.AsNoTracking().FirstOrDefaultAsync(u => u.Id == uid, ct);
            if (unit is null) throw new InvalidOperationException("Unidade não encontrada.");
        }
        var unitLabel = unit?.Name ?? "Todas as unidades";

        // Indicadores básicos
        var prevFrom = from.AddDays(-(to - from).Days - 1);
        var prevTo = from.AddDays(-1);

        // Se a unidade tem etapa de entrada configurada, contamos APENAS leads
        // que entraram nessa etapa no período (usando LeadStageHistory). Em
        // "Todas as unidades" não dá pra usar uma etapa só (cada unidade tem a
        // sua), então cai no fallback por CreatedAt.
        var entryStageId = unitId is int u0 ? await entryStageConfig.GetAsync(u0, ct) : null;
        var currentTotal = await CountLeadsAsync(tenantId, unitId, from, to, entryStageId, ct);
        var prevTotal = await CountLeadsAsync(tenantId, unitId, prevFrom, prevTo, entryStageId, ct);

        var byStage = await db.Leads.AsNoTracking()
            .Where(l => l.TenantId == tenantId && (!unitId.HasValue || l.UnitId == unitId.Value)
                        && l.CreatedAt >= from && l.CreatedAt <= to)
            .GroupBy(l => l.CurrentStage)
            .Select(g => new { stage = g.Key, count = g.Count() })
            .ToListAsync(ct);

        var byResponsavel = await db.Leads.AsNoTracking()
            .Where(l => l.TenantId == tenantId && (!unitId.HasValue || l.UnitId == unitId.Value)
                        && l.CreatedAt >= from && l.CreatedAt <= to
                        && l.AttendantId != null)
            .GroupBy(l => l.AttendantId)
            .Select(g => new { att = g.Key, count = g.Count() })
            .OrderByDescending(x => x.count)
            .Take(8)
            .ToListAsync(ct);

        var bySource = await db.Leads.AsNoTracking()
            .Where(l => l.TenantId == tenantId && (!unitId.HasValue || l.UnitId == unitId.Value)
                        && l.CreatedAt >= from && l.CreatedAt <= to)
            .GroupBy(l => l.Source)
            .Select(g => new { source = g.Key, count = g.Count() })
            .OrderByDescending(x => x.count)
            .Take(8)
            .ToListAsync(ct);

        var byCampaign = await db.Leads.AsNoTracking()
            .Where(l => l.TenantId == tenantId && (!unitId.HasValue || l.UnitId == unitId.Value)
                        && l.CreatedAt >= from && l.CreatedAt <= to)
            .GroupBy(l => l.Campaign)
            .Select(g => new { campaign = g.Key, count = g.Count() })
            .OrderByDescending(x => x.count)
            .Take(8)
            .ToListAsync(ct);

        // Distribuição por hora e dia da semana (CreatedAt)
        var rawTimes = await db.Leads.AsNoTracking()
            .Where(l => l.TenantId == tenantId && (!unitId.HasValue || l.UnitId == unitId.Value)
                        && l.CreatedAt >= from && l.CreatedAt <= to)
            .Select(l => l.CreatedAt)
            .ToListAsync(ct);

        var byHour = rawTimes.GroupBy(t => t.Hour)
            .Select(g => new { hour = g.Key, count = g.Count() })
            .OrderByDescending(x => x.count)
            .Take(5)
            .ToList();

        var byWeekday = rawTimes.GroupBy(t => (int)t.DayOfWeek)
            .Select(g => new { weekday = g.Key, count = g.Count() })
            .OrderByDescending(x => x.count)
            .ToList();

        // Cross analysis dos custom fields
        var cross = await kpiService.CustomFieldsCrossAnalysisAsync(tenantId, unitId, from, to, 8, ct);

        // Monta o "memorando de fatos". Datas no relatório aparecem em BRT
        // (o que o usuário vê no relógio), não em UTC.
        var brOffset = TimeSpan.FromHours(-3);
        var sb = new StringBuilder();
        sb.AppendLine($"# Dados de {unitLabel}");
        sb.AppendLine($"Período analisado (BRT): {from.Add(brOffset):dd/MM/yyyy} → {to.Add(brOffset):dd/MM/yyyy} ({(to - from).Days + 1} dias)");
        sb.AppendLine($"Período anterior (comparativo, BRT): {prevFrom.Add(brOffset):dd/MM/yyyy} → {prevTo.Add(brOffset):dd/MM/yyyy}");
        sb.AppendLine();
        sb.AppendLine($"## Volume");
        sb.AppendLine($"- Leads no período: **{currentTotal}**");
        sb.AppendLine($"- Leads no período anterior: {prevTotal}");
        sb.AppendLine($"- Variação: {Variation(currentTotal, prevTotal)}");
        sb.AppendLine();

        sb.AppendLine("## Distribuição por etapa atual do funil");
        foreach (var s in byStage.OrderByDescending(x => x.count))
            sb.AppendLine($"- {s.stage ?? "(sem etapa)"}: {s.count}");
        sb.AppendLine();

        sb.AppendLine("## Top atendentes responsáveis (por volume)");
        foreach (var r in byResponsavel)
            sb.AppendLine($"- Atendente {r.att}: {r.count} leads");
        sb.AppendLine();

        sb.AppendLine("## Top origens (canais)");
        foreach (var s in bySource)
            sb.AppendLine($"- {s.source}: {s.count}");
        sb.AppendLine();

        sb.AppendLine("## Top campanhas");
        foreach (var c in byCampaign)
            sb.AppendLine($"- {c.campaign}: {c.count}");
        sb.AppendLine();

        sb.AppendLine("## Distribuição por horário (top 5 horas)");
        foreach (var h in byHour)
            sb.AppendLine($"- {h.hour:00}h: {h.count} leads");
        sb.AppendLine();

        sb.AppendLine("## Distribuição por dia da semana");
        var weekdayNames = new[] { "Domingo", "Segunda", "Terça", "Quarta", "Quinta", "Sexta", "Sábado" };
        foreach (var w in byWeekday)
            sb.AppendLine($"- {weekdayNames[w.weekday]}: {w.count} leads");
        sb.AppendLine();

        if (cross.SexoByOutcome.Count > 0)
        {
            sb.AppendLine("## Perfil — Sexo × desfecho");
            foreach (var r in cross.SexoByOutcome)
                sb.AppendLine($"- {r.Sexo}: total {r.Total} · agendou {r.Agendou} · compareceu {r.Compareceu} · fechou {r.Fechou} · faltou {r.Faltou}");
            sb.AppendLine();
        }

        if (cross.TratamentoIndicado.Count > 0)
        {
            sb.AppendLine("## Top tratamentos indicados");
            foreach (var t in cross.TratamentoIndicado) sb.AppendLine($"- {t.Value}: {t.Count}");
            sb.AppendLine();
        }

        if (cross.MotivoNaoAgendamento.Count > 0)
        {
            sb.AppendLine("## Motivos para não agendar (top)");
            foreach (var m in cross.MotivoNaoAgendamento) sb.AppendLine($"- {m.Value}: {m.Count}");
            sb.AppendLine();
        }

        if (cross.Profissao.Count > 0)
        {
            sb.AppendLine("## Profissões mais comuns");
            foreach (var p in cross.Profissao) sb.AppendLine($"- {p.Value}: {p.Count}");
            sb.AppendLine();
        }

        if (cross.Qualificacao.Count > 0)
        {
            sb.AppendLine("## Qualificação do lead (quente/morno/frio)");
            foreach (var q in cross.Qualificacao) sb.AppendLine($"- {q.Value}: {q.Count}");
            sb.AppendLine();
        }

        var facts = sb.ToString();
        logger.LogInformation("[ai-analytics] unit={Unit} contexto={Bytes}B", unit?.Id, facts.Length);

        var userPrompt =
            $"Analise os dados abaixo de **{unitLabel}** e gere o relatório.\n\n" +
            "```\n" + facts + "\n```";

        return await openAi.ChatAsync(apiKey, SystemPrompt, userPrompt, ct,
            model: OpenAiClient.DefaultModel, temperature: 0.4, maxTokens: 1800);
    }

    /// <summary>
    /// Conta leads do período. Se <paramref name="entryStageId"/> está definido,
    /// conta APENAS leads que entraram nessa etapa (via LeadStageHistory) —
    /// é o número que bate com o widget "leads de entrada" da Kommo. Senão,
    /// fallback no CreatedAt (qualquer lead criado no período).
    /// </summary>
    private async Task<int> CountLeadsAsync(int tenantId, int? unitId, DateTime from, DateTime to, int? entryStageId, CancellationToken ct)
    {
        // entryStageId só vem preenchido quando há uma unidade específica.
        if (entryStageId is int stageId && unitId is int sUid)
        {
            return await db.LeadStageHistories.AsNoTracking()
                .Where(h => h.StageId == stageId
                            && h.ChangedAt >= from && h.ChangedAt <= to
                            && h.Lead.TenantId == tenantId
                            && h.Lead.UnitId == sUid)
                .Select(h => h.LeadId)
                .Distinct()
                .CountAsync(ct);
        }
        return await db.Leads.AsNoTracking().CountAsync(l =>
            l.TenantId == tenantId && (!unitId.HasValue || l.UnitId == unitId.Value)
            && l.CreatedAt >= from && l.CreatedAt <= to, ct);
    }

    /// <summary>
    /// Fatos rápidos pra alimentar o contexto do CHAT — sem chamar GPT.
    /// Custa só algumas queries leves; serve pra colar no system prompt do chat
    /// e a I.A. responder perguntas como "quantos leads esse mês?".
    /// </summary>
    public async Task<string> BuildChatFactsAsync(int tenantId, int unitId, DateTime from, DateTime to, CancellationToken ct)
    {
        from = AsUtc(from); to = AsUtc(to);
        var unit = await db.Units.AsNoTracking().FirstOrDefaultAsync(u => u.Id == unitId, ct);
        if (unit is null) return string.Empty;

        var entryStageId = await entryStageConfig.GetAsync(unitId, ct);
        var total = await CountLeadsAsync(tenantId, unitId, from, to, entryStageId, ct);
        var topStages = await db.Leads.AsNoTracking()
            .Where(l => l.TenantId == tenantId && l.UnitId == unitId
                        && l.CreatedAt >= from && l.CreatedAt <= to)
            .GroupBy(l => l.CurrentStage)
            .Select(g => new { stage = g.Key, count = g.Count() })
            .OrderByDescending(x => x.count)
            .Take(6)
            .ToListAsync(ct);

        var topSources = await db.Leads.AsNoTracking()
            .Where(l => l.TenantId == tenantId && l.UnitId == unitId
                        && l.CreatedAt >= from && l.CreatedAt <= to)
            .GroupBy(l => l.Source)
            .Select(g => new { src = g.Key, count = g.Count() })
            .OrderByDescending(x => x.count)
            .Take(5)
            .ToListAsync(ct);

        // Horário/dia de chegada — alimenta o preset "Melhores horários".
        // (CreatedAt está em UTC; o relatório usa hora local BRT = UTC-3.)
        var rawTimes = await db.Leads.AsNoTracking()
            .Where(l => l.TenantId == tenantId && l.UnitId == unitId
                        && l.CreatedAt >= from && l.CreatedAt <= to)
            .Select(l => l.CreatedAt)
            .ToListAsync(ct);
        var brOffset = TimeSpan.FromHours(-3);
        var byHour = rawTimes.GroupBy(t => t.Add(brOffset).Hour)
            .Select(g => new { hour = g.Key, count = g.Count() })
            .OrderByDescending(x => x.count)
            .Take(5)
            .ToList();
        var byWeekday = rawTimes.GroupBy(t => (int)t.Add(brOffset).DayOfWeek)
            .Select(g => new { weekday = g.Key, count = g.Count() })
            .OrderByDescending(x => x.count)
            .ToList();

        // Tempo de conversão (CreatedAt → ConvertedAt) — alimenta o preset
        // "Quem converteu mais rápido".
        var converted = await db.Leads.AsNoTracking()
            .Where(l => l.TenantId == tenantId && l.UnitId == unitId
                        && l.CreatedAt >= from && l.CreatedAt <= to
                        && l.ConvertedAt != null)
            .Select(l => new { l.Name, l.Source, l.CreatedAt, Converted = l.ConvertedAt!.Value })
            .ToListAsync(ct);
        var fastest = converted
            .Select(l => new { l.Name, l.Source, Hours = (l.Converted - l.CreatedAt).TotalHours })
            .Where(x => x.Hours >= 0)
            .OrderBy(x => x.Hours)
            .Take(10)
            .ToList();

        // Atendente "bombando" — vem do custom field "Responsável agendamento"
        // (nomes reais), não do AttendantId numérico.
        var cross = await kpiService.CustomFieldsCrossAnalysisAsync(tenantId, unitId, from, to, 8, ct);

        var sb = new StringBuilder();
        sb.AppendLine($"## Números rápidos da unidade {unit.Name}");
        sb.AppendLine($"- Total de leads no período: {total}");
        if (topStages.Count > 0)
        {
            sb.AppendLine("- Por etapa do funil:");
            foreach (var s in topStages) sb.AppendLine($"  - {s.stage ?? "(sem etapa)"}: {s.count}");
        }
        if (topSources.Count > 0)
        {
            sb.AppendLine("- Top origens:");
            foreach (var s in topSources) sb.AppendLine($"  - {s.src}: {s.count}");
        }
        if (byHour.Count > 0)
        {
            sb.AppendLine("- Melhores horários de chegada de leads (hora BRT, top 5):");
            foreach (var h in byHour) sb.AppendLine($"  - {h.hour:00}h: {h.count} leads");
        }
        if (byWeekday.Count > 0)
        {
            var weekdayNames = new[] { "Domingo", "Segunda", "Terça", "Quarta", "Quinta", "Sexta", "Sábado" };
            sb.AppendLine("- Por dia da semana:");
            foreach (var w in byWeekday) sb.AppendLine($"  - {weekdayNames[w.weekday]}: {w.count} leads");
        }
        if (cross.ResponsavelAgendamento.Count > 0)
        {
            sb.AppendLine("- Atendentes (Responsável pelo agendamento, por volume):");
            foreach (var r in cross.ResponsavelAgendamento) sb.AppendLine($"  - {r.Value}: {r.Count}");
        }
        if (fastest.Count > 0)
        {
            sb.AppendLine("- Quem converteu mais rápido (entrada → conversão, top 10):");
            foreach (var f in fastest)
                sb.AppendLine($"  - {f.Name} ({f.Source}): {f.Hours.ToString("F1", CultureInfo.InvariantCulture)}h");
        }
        else
        {
            sb.AppendLine("- Tempo de conversão: nenhum lead com data de conversão registrada no período.");
        }
        return sb.ToString();
    }

    /// <summary>Garante Kind=Utc — exigência do Npgsql pra timestamp with time zone.</summary>
    private static DateTime AsUtc(DateTime d) =>
        d.Kind == DateTimeKind.Utc ? d : DateTime.SpecifyKind(d, DateTimeKind.Utc);

    private static string Variation(int current, int prev)
    {
        if (prev == 0) return current == 0 ? "estável" : "novo (sem comparativo)";
        var pct = 100.0 * (current - prev) / prev;
        var arrow = pct > 0 ? "↑" : pct < 0 ? "↓" : "→";
        return $"{arrow} {pct.ToString("F1", CultureInfo.InvariantCulture)}%";
    }
}
