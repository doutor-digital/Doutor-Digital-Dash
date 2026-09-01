using System.Text.Json;
using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.DTOs.Saude;
using LeadAnalytics.Api.Models;
using LeadAnalytics.Api.Service.Ads;
using LeadAnalytics.Api.Service.Spine;
using Microsoft.EntityFrameworkCore;

namespace LeadAnalytics.Api.Service;

/// <summary>
/// O relatório que uma clínica precisa de verdade, num lugar só.
///
/// POR QUE NÃO É A ANÁLISE DA IA
/// -----------------------------
/// A IA escreve bem e às vezes mistura período com base inteira — o relatório de 05 a 06/08
/// listava "não interagiu: 1.576", que é a base toda. Aqui não existe redação: cada número sai
/// de uma consulta com o mesmo filtro de data, e vem acompanhado dos NOMES que o compõem.
///
/// O NOME É O QUE TORNA O NÚMERO AUDITÁVEL
/// ---------------------------------------
/// "12 leads sem origem" ninguém consegue conferir. Com a lista de nomes, a gerente abre a
/// Kommo, procura o primeiro e vê se bate. Foi exatamente isso que o dono pediu, e é o que
/// separa relatório de boletim de propaganda.
/// </summary>
public class RelatorioCompletoService(
    AppDbContext db,
    KpiConfigService kpiConfig,
    CampanhasService campanhas,
    AnunciosDesempenhoService anuncios)
{
    private readonly AppDbContext _db = db;
    private readonly KpiConfigService _kpiConfig = kpiConfig;
    private readonly CampanhasService _campanhas = campanhas;
    private readonly AnunciosDesempenhoService _anuncios = anuncios;

    /// <summary>Quantos nomes por lacuna. O bastante para conferir, não tanto que vire lista telefônica.</summary>
    private const int NomesPorLacuna = 40;

    public async Task<RelatorioCompletoDto> GetAsync(
        int tenantId, int? unitId, DateTime de, DateTime ate, CancellationToken ct = default)
    {
        // A data vem da query string sem fuso (Kind=Unspecified) e o Npgsql recusa:
        // "Cannot write DateTime with Kind=Unspecified to PostgreSQL type
        // 'timestamp with time zone'". Sem isto a rota devolve 500 sempre.
        de = DateTime.SpecifyKind(de, DateTimeKind.Utc);
        ate = DateTime.SpecifyKind(ate, DateTimeKind.Utc);

        var mapa = unitId.HasValue
            ? await _kpiConfig.GetLeadProfileConfigAsync(unitId.Value, ct)
            : new KpiConfigService.LeadProfileFields();

        var leads = await _db.Leads.AsNoTracking().ExcludeDeleted()
            .Where(l => l.TenantId == tenantId
                        && (!unitId.HasValue || l.UnitId == unitId.Value)
                        && (l.OriginalCreatedAt ?? l.CreatedAt) >= de
                        && (l.OriginalCreatedAt ?? l.CreatedAt) <= ate)
            .Select(l => new
            {
                l.Id, l.Name, l.Phone, l.CurrentStage, l.Qualification,
                l.AppointmentScheduledAt, l.CustomFieldsJson,
                Criado = l.OriginalCreatedAt ?? l.CreatedAt,
            })
            .ToListAsync(ct);

        var dto = new RelatorioCompletoDto
        {
            De = de,
            Ate = ate,
            TotalLeads = leads.Count,
        };

        // ── Movimento: entradas e agendamentos com data confiável ───────────
        dto.Agendaram = await _db.LeadStageHistories.AsNoTracking()
            .Where(h => h.EntrySource != LeadStageHistory.SourceLegacy
                        // Data corrigida manda: a conferência tem de olhar o MESMO
                        // número que o card, senão ela acusa divergência que não existe.
                        && (h.CorrectedChangedAt ?? h.ChangedAt) >= de
                        && (h.CorrectedChangedAt ?? h.ChangedAt) <= ate
                        && h.StageLabel.Contains("AGENDADO")
                        && h.Lead.TenantId == tenantId
                        && (!unitId.HasValue || h.Lead.UnitId == unitId.Value))
            .Select(h => h.LeadId).Distinct().CountAsync(ct);

        // ── Agenda da clínica: é ela que sabe quem apareceu ─────────────────
        if (unitId is int uid)
        {
            var d1 = SpineApiClient.DiaLocal(de);
            var d2 = SpineApiClient.DiaLocal(ate);
            var agenda = await _db.SpineScheduleSnapshots.AsNoTracking()
                .Where(s => s.UnitId == uid && s.DiaLocal >= d1 && s.DiaLocal <= d2)
                .Select(s => s.IdStatus)
                .ToListAsync(ct);

            dto.HorariosNaClinica = agenda.Count;
            dto.Compareceram = agenda.Count(x => x == SpineApiClient.ScheduleStatus.Atendido);
            dto.HorariosPerdidos = agenda.Count(x =>
                x == SpineApiClient.ScheduleStatus.Desmarcado
                || x == SpineApiClient.ScheduleStatus.Remarcado
                || x == SpineApiClient.ScheduleStatus.NaoCompareceu);
        }

        dto.Campanhas = await _campanhas.GetAsync(tenantId, unitId, de, ate, ct);
        dto.Anuncios = await _anuncios.GetAsync(
            tenantId, unitId, DateOnly.FromDateTime(de), DateOnly.FromDateTime(ate), ct);

        // ── Lacunas: o que não foi preenchido, COM NOME ─────────────────────
        string? Campo(string? json, long? id, Func<string, bool> porNome) =>
            KpiConfigService.ExtractFieldPublic(json, id, porNome);

        static bool Vazio(string? s) => string.IsNullOrWhiteSpace(s);
        static bool Agendado(string? etapa) =>
            (etapa ?? "").Contains("AGENDADO", StringComparison.OrdinalIgnoreCase);

        void Lacuna(string campo, string porque, Func<dynamic, bool> falta, int universo)
        {
            var quem = leads.Where(l => falta(l)).ToList();
            if (quem.Count == 0) return;

            dto.Lacunas.Add(new LacunaDto
            {
                Campo = campo,
                Porque = porque,
                Faltando = quem.Count,
                Universo = universo,
                Percentual = universo == 0 ? 0 : Math.Round(100.0 * quem.Count / universo, 1),
                Leads = [.. quem
                    .OrderByDescending(l => (DateTime)l.Criado)
                    .Take(NomesPorLacuna)
                    .Select(l => new LeadSemCampoDto
                    {
                        LeadId = (int)l.Id,
                        Nome = (string?)l.Name,
                        Telefone = (string?)l.Phone,
                        Etapa = (string?)l.CurrentStage,
                        Criado = (DateTime)l.Criado,
                    })],
            });
        }

        var totalAgendados = leads.Count(l => Agendado(l.CurrentStage));

        Lacuna("Origem", "Sem origem o lead some da conta por canal e o custo por lead fica errado.",
            l => Vazio(Campo(l.CustomFieldsJson, mapa.OrigemFieldId, n => n.Contains("origem"))),
            leads.Count);

        Lacuna("Qualificação", "Ninguém disse se é quente, morno ou frio — não dá para priorizar a fila.",
            l => Vazio((string?)l.Qualification)
                 && Vazio(Campo(l.CustomFieldsJson, mapa.QualificacaoFieldId, n => n.Contains("qualifica"))),
            leads.Count);

        Lacuna("Tipo de lead", "Sem o tipo, cadastro e resgate se misturam em todos os cards.",
            l => Vazio(Campo(l.CustomFieldsJson, mapa.TipoFieldId, n => n.Contains("tipo") && n.Contains("lead"))),
            leads.Count);

        Lacuna("Data da consulta", "Agendado sem data não entra em lembrete e vira falta sem aviso.",
            l => Agendado((string?)l.CurrentStage) && l.AppointmentScheduledAt == null,
            totalAgendados);

        Lacuna("Responsável pelo agendamento", "Sem dono não há ranking por SDR nem cobrança individual.",
            l => Agendado((string?)l.CurrentStage)
                 && Vazio(Campo(l.CustomFieldsJson, mapa.ResponsavelFieldId,
                                n => n.Contains("respons") && n.Contains("agendamento"))),
            totalAgendados);

        Lacuna("Motivo do não agendamento", "Perda sem motivo escrito não vira aprendizado nenhum.",
            l => ((string?)l.CurrentStage ?? "").Contains("PERDIDO", StringComparison.OrdinalIgnoreCase)
                 && Vazio(Campo(l.CustomFieldsJson, mapa.MotivoNaoAgendamentoFieldId, n => n.Contains("motivo"))),
            leads.Count(l => ((string?)l.CurrentStage ?? "").Contains("PERDIDO", StringComparison.OrdinalIgnoreCase)));

        Lacuna("Telefone", "Sem telefone não dá para ligar nem juntar duplicado.",
            l => Vazio((string?)l.Phone) || (string?)l.Phone == "AGUARDANDO_COLETA",
            leads.Count);

        dto.Lacunas = [.. dto.Lacunas.OrderByDescending(x => x.Faltando)];

        // ── Origens do período ──────────────────────────────────────────────
        var origens = new Dictionary<string, int>();
        foreach (var l in leads)
        {
            var o = Campo(l.CustomFieldsJson, mapa.OrigemFieldId, n => n.Contains("origem"));
            var chave = Vazio(o) ? "(sem origem)" : o!.Trim();
            origens[chave] = origens.GetValueOrDefault(chave) + 1;
        }
        dto.Origens = [.. origens.OrderByDescending(x => x.Value)
            .Select(x => new ValorContagemDto { Valor = x.Key, Contagem = x.Value })];

        return dto;
    }
}
