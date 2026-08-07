using System.Text.Json;
using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.DTOs.Jornada;
using LeadAnalytics.Api.Models;
using LeadAnalytics.Api.Service.Spine;
using Microsoft.EntityFrameworkCore;

namespace LeadAnalytics.Api.Service;

/// <summary>
/// A vida de um lead: por onde passou, quanto tempo levou em cada passo e se a IA está com ele.
///
/// O QUE ISTO RESPONDE QUE OS CARDS NÃO RESPONDEM
/// ----------------------------------------------
/// O dashboard inteiro é agregado. Quando alguém pergunta "o que aconteceu com ESTE paciente",
/// não havia onde olhar sem abrir a Kommo. Aqui entra o telefone, o nome ou o número do lead e
/// sai a linha do tempo: entrou 14:02, qualificado 14:08 (6 min), agendado 14:40 (32 min).
///
/// SÓ DATA CONFIÁVEL VIRA TEMPO
/// ----------------------------
/// <see cref="LeadStageHistory"/> tem três procedências. Só webhook e API de eventos guardam o
/// instante real da transição; as linhas legadas guardam a data do sync. Medido em Imperatriz:
/// de 25 342 linhas, 12 584 são confiáveis. Usar as outras produziria "ficou 0 min em
/// qualificação" para metade da base — pior que não mostrar.
///
/// MOVIMENTAÇÃO EM LOTE NÃO É ATENDIMENTO
/// -------------------------------------
/// Em 24/07/2026 a migração de funil moveu 7 686 leads para TRATAMENTO_CANCELADO no mesmo dia.
/// Tecnicamente é uma transição registrada; humanamente não aconteceu nada. Cada passo vem
/// marcado com <see cref="JornadaPassoDto.EmLote"/> quando dividiu o minuto com muitos outros,
/// e a tela avisa em vez de contar aquilo como tempo de atendimento.
///
/// O QUE AINDA NÃO DÁ PARA RESPONDER
/// --------------------------------
/// "Qual mensagem converteu este lead" não tem resposta hoje: não existe uma única mensagem
/// gravada para Imperatriz (0 conversas do agente, 0 mensagens). O Salesbot ainda não dispara
/// para a Sofia. O DTO devolve <see cref="JornadaIaDto.SemRegistro"/> para a tela poder dizer
/// isso em vez de mostrar um vazio ambíguo.
/// </summary>
public class JornadaService(AppDbContext db, KpiConfigService kpiConfig)
{
    private readonly AppDbContext _db = db;
    private readonly KpiConfigService _kpiConfig = kpiConfig;

    /// <summary>
    /// A partir de quantos leads no mesmo minuto e mesma etapa a transição vira "lote".
    /// Uma SDR não move dez leads para a mesma etapa no mesmo minuto; um script move.
    /// </summary>
    private const int LimiteLote = 10;

    // ─── Busca ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Acha o lead por telefone, nome, número do lead ou número dele na Kommo.
    /// Telefone é comparado só por dígitos: quem digita "99 98416-3576" tem que achar.
    /// </summary>
    public async Task<List<JornadaBuscaItemDto>> BuscarAsync(
        int tenantId, int? unitId, string termo, CancellationToken ct = default)
    {
        termo = (termo ?? string.Empty).Trim();
        if (termo.Length < 3) return [];

        var q = _db.Leads.AsNoTracking()
            .Where(l => l.TenantId == tenantId && (!unitId.HasValue || l.UnitId == unitId.Value));

        var digitos = new string([.. termo.Where(char.IsDigit)]);
        var soDigitos = digitos.Length == termo.Length;

        // Número na Kommo e número do lead são inteiros; telefone é texto. Um termo só de
        // dígitos pode ser qualquer um dos três, então procura nos três.
        int.TryParse(termo, out var comoNumero);

        if (soDigitos && digitos.Length >= 6)
        {
            // Telefone: casa pelo fim do número, que é a parte que não muda com DDI/DDD.
            var fim = digitos.Length > 8 ? digitos[^8..] : digitos;
            q = q.Where(l => l.Phone.Contains(fim)
                             || l.ExternalId == comoNumero
                             || l.Id == comoNumero);
        }
        else if (soDigitos)
        {
            q = q.Where(l => l.ExternalId == comoNumero
                             || l.Id == comoNumero
                             || l.Phone.Contains(digitos));
        }
        else
        {
            q = q.Where(l => EF.Functions.ILike(l.Name, $"%{termo}%"));
        }

        return await q
            .OrderByDescending(l => l.CreatedAt)
            .Take(25)
            .Select(l => new JornadaBuscaItemDto
            {
                LeadId = l.Id,
                KommoId = l.ExternalId.ToString(),
                Nome = l.Name,
                Telefone = l.Phone,
                EtapaAtual = l.CurrentStage,
                CriadoEm = l.CreatedAt,
            })
            .ToListAsync(ct);
    }

    // ─── Jornada de um lead ──────────────────────────────────────────────────

    public async Task<JornadaDto?> GetAsync(
        int tenantId, int leadId, CancellationToken ct = default)
    {
        var lead = await _db.Leads.AsNoTracking()
            .Include(l => l.Attendant)
            .FirstOrDefaultAsync(l => l.Id == leadId && l.TenantId == tenantId, ct);

        if (lead is null) return null;

        var agora = DateTime.UtcNow;

        // ── Passos, só com data confiável ────────────────────────────────────
        var brutos = await _db.LeadStageHistories.AsNoTracking()
            .Where(h => h.LeadId == leadId)
            .OrderBy(h => h.ChangedAt)
            .Select(h => new { h.StageLabel, h.ChangedAt, h.EntrySource })
            .ToListAsync(ct);

        var confiaveis = brutos
            .Where(h => h.EntrySource != LeadStageHistory.SourceLegacy)
            .ToList();

        // Quantos outros leads foram para a mesma etapa no mesmo minuto? Acima do limite,
        // foi script, não pessoa.
        var lotes = new Dictionary<(string, DateTime), int>();
        foreach (var h in confiaveis)
        {
            var minuto = new DateTime(h.ChangedAt.Year, h.ChangedAt.Month, h.ChangedAt.Day,
                                      h.ChangedAt.Hour, h.ChangedAt.Minute, 0, DateTimeKind.Utc);
            // Dois passos no mesmo minuto e mesma etapa contam uma vez só.
            if (lotes.ContainsKey((h.StageLabel, minuto))) continue;
            var fim = minuto.AddMinutes(1);
            var quantos = await _db.LeadStageHistories.AsNoTracking()
                .CountAsync(x => x.StageLabel == h.StageLabel
                                 && x.ChangedAt >= minuto && x.ChangedAt < fim
                                 && x.Lead.UnitId == lead.UnitId, ct);
            lotes[(h.StageLabel, minuto)] = quantos;
        }

        var passos = new List<JornadaPassoDto>();
        for (int i = 0; i < confiaveis.Count; i++)
        {
            var atual = confiaveis[i];
            var saida = i + 1 < confiaveis.Count ? confiaveis[i + 1].ChangedAt : (DateTime?)null;
            var minuto = new DateTime(atual.ChangedAt.Year, atual.ChangedAt.Month, atual.ChangedAt.Day,
                                      atual.ChangedAt.Hour, atual.ChangedAt.Minute, 0, DateTimeKind.Utc);
            var noLote = lotes.GetValueOrDefault((atual.StageLabel, minuto), 1);

            passos.Add(new JornadaPassoDto
            {
                Etapa = Rotulo(atual.StageLabel),
                EtapaCrua = atual.StageLabel,
                Entrou = atual.ChangedAt,
                Saiu = saida,
                MinutosAte = saida is DateTime s
                    ? Math.Round((s - atual.ChangedAt).TotalMinutes, 1)
                    : Math.Round((agora - atual.ChangedAt).TotalMinutes, 1),
                Atual = saida is null,
                EmLote = noLote >= LimiteLote,
                NoMesmoMinuto = noLote,
                // Etapa que virou id numérico cru = status apagado na Kommo. Não é jornada,
                // é resíduo de funil, e a tela precisa poder mostrar isso de outro jeito.
                Orfa = atual.StageLabel.Length >= 6 && atual.StageLabel.All(char.IsDigit),
            });
        }

        // ── Estado da IA ─────────────────────────────────────────────────────
        var ia = await EstadoDaIaAsync(lead, ct);

        // ── Tempos que interessam ────────────────────────────────────────────
        var primeiroReal = passos.FirstOrDefault(p => !p.EmLote);
        var agendou = passos.FirstOrDefault(p =>
            p.EtapaCrua.Contains("AGENDADO", StringComparison.OrdinalIgnoreCase) && !p.EmLote);

        return new JornadaDto
        {
            LeadId = lead.Id,
            KommoId = lead.ExternalId.ToString(),
            Nome = lead.Name,
            Telefone = lead.Phone,
            Origem = lead.Source,
            Tipo = lead.LeadType,
            EtapaAtual = Rotulo(lead.CurrentStage),
            Responsavel = lead.Attendant?.Name,
            CriadoEm = lead.CreatedAt,
            DataConsulta = lead.AppointmentScheduledAt,
            Qualificacao = lead.Qualification,

            Passos = passos,
            PassosDescartados = brutos.Count - confiaveis.Count,

            // "Parado há" = desde a última coisa registrada. É o que temos: não existe
            // registro de mensagem recebida para esta unidade, então prometer "sem resposta
            // há X" seria inventar.
            MinutosParado = passos.Count > 0
                ? Math.Round((agora - passos[^1].Entrou).TotalMinutes, 1)
                : Math.Round((agora - lead.CreatedAt).TotalMinutes, 1),

            MinutosAtePrimeiroMovimento = primeiroReal is not null
                ? Math.Round((primeiroReal.Entrou - lead.CreatedAt).TotalMinutes, 1)
                : null,
            MinutosAteAgendar = agendou is not null
                ? Math.Round((agendou.Entrou - lead.CreatedAt).TotalMinutes, 1)
                : null,

            Ia = ia,
        };
    }

    /// <summary>
    /// A Sofia está com este lead?
    ///
    /// A resposta vem do campo booleano "Pausar IA" da Kommo, mapeado por id nas Configurações
    /// Técnicas. Por id, e não por nome: em Imperatriz existem dois campos com esse mesmo nome,
    /// e o herdado está marcado em 7 624 leads contra 89 do que o agente realmente lê.
    /// </summary>
    private async Task<JornadaIaDto> EstadoDaIaAsync(Lead lead, CancellationToken ct)
    {
        var ia = new JornadaIaDto();

        if (lead.UnitId is int uid)
        {
            var mapa = await _kpiConfig.GetLeadProfileConfigAsync(uid, ct);
            if (mapa.PausarIaFieldId is long fid)
            {
                var bruto = LerCampo(lead.CustomFieldsJson, fid);
                ia.Pausada = bruto is not null
                    && (bruto.Equals("true", StringComparison.OrdinalIgnoreCase) || bruto == "1");
                ia.CampoMapeado = true;
            }
        }

        var conversas = await _db.AgentConversations.AsNoTracking()
            .Where(c => c.LeadId == lead.Id)
            .OrderByDescending(c => c.LastMessageAt ?? c.StartedAt)
            .Select(c => new { c.Id, c.MessageCount, c.HandedOff, c.LastMessageAt, c.Summary })
            .FirstOrDefaultAsync(ct);

        if (conversas is not null)
        {
            ia.ConversaId = conversas.Id;
            ia.Mensagens = conversas.MessageCount;
            ia.PassouParaHumano = conversas.HandedOff;
            ia.UltimaMensagemEm = conversas.LastMessageAt;
            ia.Resumo = conversas.Summary;
        }
        else
        {
            // Sem conversa nenhuma. Não é "a IA não falou": é que nada foi gravado — em
            // Imperatriz o Salesbot ainda não chama a Sofia. A tela precisa saber a diferença.
            ia.SemRegistro = true;
        }

        return ia;
    }

    // ─── Ranking: quem converteu mais rápido ─────────────────────────────────

    /// <summary>
    /// Os leads que menos tempo levaram entre entrar e chegar em AGENDADO, no período.
    ///
    /// Conversão aqui é chegar em agendado, não "ganho": nenhum lead de Imperatriz tem data de
    /// conversão gravada (<c>ConvertedAt</c> é nulo em 8 755 de 8 755), então usar aquele campo
    /// devolveria lista vazia para sempre.
    /// </summary>
    public async Task<List<JornadaRankingItemDto>> RankingAsync(
        int tenantId, int? unitId, DateTime de, DateTime ate, CancellationToken ct = default)
    {
        // A data vem da query string sem fuso (Kind=Unspecified) e o Npgsql recusa:
        // "Cannot write DateTime with Kind=Unspecified to PostgreSQL type
        // 'timestamp with time zone'". Sem isto a rota devolve 500 sempre.
        de = DateTime.SpecifyKind(de, DateTimeKind.Utc);
        ate = DateTime.SpecifyKind(ate, DateTimeKind.Utc);

        var candidatos = await _db.LeadStageHistories.AsNoTracking()
            .Where(h => h.EntrySource != LeadStageHistory.SourceLegacy
                        && h.ChangedAt >= de && h.ChangedAt <= ate
                        && h.StageLabel.Contains("AGENDADO")
                        && h.Lead.TenantId == tenantId
                        && (!unitId.HasValue || h.Lead.UnitId == unitId.Value))
            .Select(h => new
            {
                h.LeadId,
                h.ChangedAt,
                h.StageLabel,
                h.Lead.Name,
                h.Lead.Phone,
                h.Lead.Source,
                h.Lead.CreatedAt,
            })
            .ToListAsync(ct);

        // Primeira chegada em agendado por lead — voltar para a etapa não recomeça a contagem.
        var porLead = candidatos
            .GroupBy(c => c.LeadId)
            .Select(g => g.OrderBy(x => x.ChangedAt).First())
            .Where(c => c.ChangedAt > c.CreatedAt)
            .Select(c => new JornadaRankingItemDto
            {
                LeadId = c.LeadId,
                Nome = c.Name,
                Telefone = c.Phone,
                Origem = c.Source,
                CriadoEm = c.CreatedAt,
                AgendouEm = c.ChangedAt,
                Minutos = Math.Round((c.ChangedAt - c.CreatedAt).TotalMinutes, 1),
                CadastradoJaAgendado = (c.ChangedAt - c.CreatedAt).TotalMinutes < 2,
            })
            // Cadastrado-já-agendado vai para o fim: é registro atrasado, não velocidade.
            .OrderBy(x => x.CadastradoJaAgendado)
            .ThenBy(x => x.Minutos)
            .Take(20)
            .ToList();

        return porLead;
    }

    // ─── Auxiliares ──────────────────────────────────────────────────────────

    private static string? LerCampo(string? json, long fieldId)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;
            foreach (var f in doc.RootElement.EnumerateArray())
            {
                if (!f.TryGetProperty("field_id", out var id)) continue;
                var idVal = id.ValueKind == JsonValueKind.Number
                    ? id.GetInt64()
                    : long.TryParse(id.GetString(), out var p) ? p : -1;
                if (idVal != fieldId) continue;

                if (f.TryGetProperty("value", out var v))
                    return v.ValueKind == JsonValueKind.String ? v.GetString() : v.ToString();
            }
        }
        catch { /* JSON torto de lead antigo não derruba a jornada */ }
        return null;
    }

    /// <summary>Etapa em id numérico cru vira texto legível — o resto passa como está.</summary>
    internal static string Rotulo(string? etapa)
    {
        var e = (etapa ?? string.Empty).Trim();
        if (e.Length == 0) return "—";
        if (e.Length >= 6 && e.All(char.IsDigit)) return $"Etapa removida ({e})";
        return e.Replace('_', ' ');
    }

    /// <summary>Fuso da clínica, para a tela não ter que converter.</summary>
    public static DateTime Local(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), SpineApiClient.BrTz);
}
