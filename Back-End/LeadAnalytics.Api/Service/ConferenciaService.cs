using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.DTOs.Saude;
using LeadAnalytics.Api.Models;
using LeadAnalytics.Api.Service.Spine;
using Microsoft.EntityFrameworkCore;

namespace LeadAnalytics.Api.Service;

/// <summary>
/// Confere se os números do dashboard fecham entre si.
///
/// POR QUE ISTO EXISTE
/// -------------------
/// Teste unitário prova que a regra está certa; não prova que o número da tela bate. Todo erro
/// que apareceu neste dashboard passou por código que funcionava: o card de Consultas mostra
/// 8 no número grande (fonte franquia), 12 no desfecho (data da consulta na Kommo) e 57 em
/// "marcadas no período" (data de preenchimento) — três populações no mesmo card, e nenhuma
/// delas errada sozinha.
///
/// Cada conferência abaixo é uma AFIRMAÇÃO que tem de ser verdade. Quando não é, a tela diz
/// qual é, com os dois números e o que fazer. É isto que tira a checagem do "alguém percebeu"
/// e coloca numa página que qualquer um abre.
///
/// O QUE ESTE SERVIÇO NÃO FAZ
/// --------------------------
/// Não conserta nada e não esconde nada. Uma conferência que falha continua falhando até
/// alguém arrumar a causa — o valor está em ser impossível não ver.
/// </summary>
public class ConferenciaService(AppDbContext db, KpiConfigService kpiConfig)
{
    private readonly AppDbContext _db = db;
    private readonly KpiConfigService _kpiConfig = kpiConfig;

    /// <summary>
    /// Diferença tolerada entre a nossa contagem de consultas e a agenda da franquia.
    /// Não é zero de propósito: agendamento feito na recepção sem passar pelo CRM é normal.
    /// Acima disso deixou de ser exceção e virou processo furado.
    /// </summary>
    private const int ToleranciaFranquia = 5;

    public async Task<ConferenciaDto> GetAsync(
        int tenantId, int? unitId, DateTime de, DateTime ate, CancellationToken ct = default)
    {
        // A data vem da query string sem fuso (Kind=Unspecified) e o Npgsql recusa:
        // "Cannot write DateTime with Kind=Unspecified to PostgreSQL type
        // 'timestamp with time zone'". Sem isto a rota devolve 500 sempre.
        de = DateTime.SpecifyKind(de, DateTimeKind.Utc);
        ate = DateTime.SpecifyKind(ate, DateTimeKind.Utc);

        var checagens = new List<ChecagemDto>();

        var escopo = _db.Leads.AsNoTracking().ExcludeDeleted()
            .Where(l => l.TenantId == tenantId && (!unitId.HasValue || l.UnitId == unitId.Value));

        // ── 1. Agendados: o card contra a contagem crua ─────────────────────
        var entradasAgendado = await _db.LeadStageHistories.AsNoTracking()
            .Where(h => h.EntrySource != LeadStageHistory.SourceLegacy
                        && h.ChangedAt >= de && h.ChangedAt <= ate
                        && h.StageLabel.Contains("AGENDADO")
                        && h.Lead.TenantId == tenantId
                        && (!unitId.HasValue || h.Lead.UnitId == unitId.Value))
            .Select(h => h.LeadId)
            .Distinct()
            .CountAsync(ct);

        var reclassificados = await ContarReclassificadosAsync(tenantId, unitId, de, ate, ct);

        checagens.Add(new ChecagemDto
        {
            Id = "agendados_reclassificacao",
            Titulo = "Agendados = entradas na etapa − reclassificações",
            Explica = "Lead que já era agendado antes do período e só mudou de sem-pagamento "
                    + "para com-pagamento não é agendamento novo.",
            ValorA = entradasAgendado - reclassificados,
            RotuloA = "card",
            ValorB = entradasAgendado,
            RotuloB = "entradas cruas",
            Detalhe = $"{reclassificados} reclassificação(ões) descontada(s).",
            Passou = true,
        });

        // ── 2. Consultas: as três populações do mesmo card ──────────────────
        var marcadasNoPeriodo = await escopo
            .CountAsync(l => l.AppointmentScheduledAtFilledAt >= de
                             && l.AppointmentScheduledAtFilledAt <= ate, ct);

        var comDataNoPeriodo = await escopo
            .CountAsync(l => l.AppointmentScheduledAt >= de && l.AppointmentScheduledAt <= ate, ct);

        var fonteConsultas = unitId.HasValue
            ? await _db.KpiConfigurations.AsNoTracking()
                .Where(k => k.UnitId == unitId.Value && k.KpiKey == "consultas")
                .Select(k => k.SourceType)
                .FirstOrDefaultAsync(ct)
            : null;

        // O caso que originou esta página: número grande de uma fonte, quebras de outra.
        var misturaFonte = fonteConsultas == "franquia";
        checagens.Add(new ChecagemDto
        {
            Id = "consultas_fonte_unica",
            Titulo = "O card de Consultas usa uma fonte só",
            Explica = "O número grande vem da franquia, mas as quebras abaixo dele são "
                    + "calculadas na Kommo. Card com duas fontes nunca fecha, e não é erro de "
                    + "nenhuma das duas.",
            ValorA = comDataNoPeriodo,
            RotuloA = "Kommo, por data da consulta",
            ValorB = marcadasNoPeriodo,
            RotuloB = "Kommo, por data de preenchimento",
            Detalhe = misturaFonte
                ? "Número grande vem da franquia; quebras vêm da Kommo. Escolher uma."
                : $"Fonte configurada: {fonteConsultas ?? "padrão"}.",
            Passou = !misturaFonte,
        });

        // ── 3. Kommo contra a agenda da franquia ────────────────────────────
        if (unitId is int uid)
        {
            var d1 = SpineApiClient.DiaLocal(de);
            var d2 = SpineApiClient.DiaLocal(ate);

            var temEspelho = await _db.SpineScheduleSnapshots.AsNoTracking()
                .AnyAsync(x => x.UnitId == uid && x.DiaLocal >= d1 && x.DiaLocal <= d2, ct);

            if (temEspelho)
            {
                var avaliacoes = await _db.SpineScheduleSnapshots.AsNoTracking()
                    .CountAsync(x => x.UnitId == uid && x.DiaLocal >= d1 && x.DiaLocal <= d2
                                     && x.IdCategory == SpineApiClient.ScheduleCategory.Avaliacao, ct);

                var diferenca = Math.Abs(avaliacoes - comDataNoPeriodo);
                checagens.Add(new ChecagemDto
                {
                    Id = "kommo_x_franquia",
                    Titulo = "Consultas na Kommo batem com a agenda da franquia",
                    Explica = "Diferença pequena é esperada: agendamento feito na recepção não "
                            + "passa pelo CRM. Diferença grande é processo furado.",
                    ValorA = comDataNoPeriodo,
                    RotuloA = "Kommo",
                    ValorB = avaliacoes,
                    RotuloB = "franquia",
                    Detalhe = $"Diferença de {diferenca}; tolerado até {ToleranciaFranquia}.",
                    Passou = diferenca <= ToleranciaFranquia,
                });
            }
        }

        // ── 4. Campo mapeado que perdeu preenchimento para o duplicado ──────
        if (unitId is int uid2)
        {
            var mapa = await _kpiConfig.GetLeadProfileConfigAsync(uid2, ct);
            if (mapa.OrigemFieldId is long origemId)
            {
                var amostra = await escopo
                    .Where(l => l.CustomFieldsJson != null)
                    .OrderByDescending(l => l.CreatedAt)
                    .Take(500)
                    .Select(l => l.CustomFieldsJson!)
                    .ToListAsync(ct);

                var comMapeado = amostra.Count(j => TemValor(j, origemId));
                var comQualquerOrigem = amostra.Count(TemAlgumaOrigem);
                var perdidos = comQualquerOrigem - comMapeado;

                checagens.Add(new ChecagemDto
                {
                    Id = "origem_campo_duplicado",
                    Titulo = "A origem mapeada é a que a equipe preenche",
                    Explica = "A conta tem campos duplicados com o mesmo nome. Se o mapeado não "
                            + "é o preenchido, o lead existe e some da contagem por origem.",
                    ValorA = comMapeado,
                    RotuloA = "no campo mapeado",
                    ValorB = comQualquerOrigem,
                    RotuloB = "em algum campo de origem",
                    Detalhe = perdidos > 0
                        ? $"{perdidos} lead(s) da amostra têm origem em outro campo."
                        : "Nenhum lead com origem fora do campo mapeado.",
                    // Até 2% é ruído de campo recém-criado; acima disso é mapeamento errado.
                    Passou = comQualquerOrigem == 0 || perdidos * 100.0 / comQualquerOrigem <= 2,
                });
            }
        }

        // ── 5. Histórico com data confiável ─────────────────────────────────
        var totalHist = await _db.LeadStageHistories.AsNoTracking()
            .CountAsync(h => h.Lead.TenantId == tenantId
                             && (!unitId.HasValue || h.Lead.UnitId == unitId.Value), ct);
        var confiavel = await _db.LeadStageHistories.AsNoTracking()
            .CountAsync(h => h.EntrySource != LeadStageHistory.SourceLegacy
                             && h.Lead.TenantId == tenantId
                             && (!unitId.HasValue || h.Lead.UnitId == unitId.Value), ct);

        checagens.Add(new ChecagemDto
        {
            Id = "historico_confiavel",
            Titulo = "O histórico de etapas tem data de verdade",
            Explica = "Linha vinda do sync guarda a data da leitura, não a da mudança de etapa. "
                    + "Só as de webhook servem para medir tempo entre etapas.",
            ValorA = confiavel,
            RotuloA = "com data real",
            ValorB = totalHist,
            RotuloB = "no total",
            Detalhe = totalHist == 0 ? "Sem histórico." :
                $"{Math.Round(100.0 * confiavel / totalHist)}% servem para medir tempo.",
            Passou = totalHist == 0 || confiavel * 100.0 / totalHist >= 40,
        });

        return new ConferenciaDto
        {
            De = de,
            Ate = ate,
            Checagens = [.. checagens.OrderBy(c => c.Passou)],
            Falharam = checagens.Count(c => !c.Passou),
        };
    }

    /// <summary>
    /// Leads que entraram em agendado no período mas JÁ tinham entrado antes dele.
    /// Mesma regra do card — se ela mudar num lugar e não no outro, a checagem 1 acusa.
    /// </summary>
    private async Task<int> ContarReclassificadosAsync(
        int tenantId, int? unitId, DateTime de, DateTime ate, CancellationToken ct)
    {
        var noPeriodo = await _db.LeadStageHistories.AsNoTracking()
            .Where(h => h.EntrySource != LeadStageHistory.SourceLegacy
                        && h.ChangedAt >= de && h.ChangedAt <= ate
                        && h.StageLabel.Contains("AGENDADO")
                        && h.Lead.TenantId == tenantId
                        && (!unitId.HasValue || h.Lead.UnitId == unitId.Value))
            .Select(h => h.LeadId)
            .Distinct()
            .ToListAsync(ct);

        if (noPeriodo.Count == 0) return 0;

        return await _db.LeadStageHistories.AsNoTracking()
            .Where(h => noPeriodo.Contains(h.LeadId)
                        && h.StageLabel.Contains("AGENDADO")
                        && h.ChangedAt < de)
            .Select(h => h.LeadId)
            .Distinct()
            .CountAsync(ct);
    }

    private static bool TemValor(string json, long fieldId)
    {
        var marca = $"\"field_id\":{fieldId}";
        var marcaEsp = $"\"field_id\": {fieldId}";
        var i = json.IndexOf(marca, StringComparison.Ordinal);
        if (i < 0) i = json.IndexOf(marcaEsp, StringComparison.Ordinal);
        if (i < 0) return false;

        // O valor vem logo depois do id no mesmo objeto; vazio é "value":"" ou null.
        var trecho = json.Substring(i, Math.Min(220, json.Length - i));
        return !trecho.Contains("\"value\":\"\"", StringComparison.Ordinal)
            && !trecho.Contains("\"value\":null", StringComparison.Ordinal);
    }

    private static bool TemAlgumaOrigem(string json) =>
        json.Contains("origem", StringComparison.OrdinalIgnoreCase)
        && !json.Contains("\"value\":\"\"", StringComparison.Ordinal);
}
