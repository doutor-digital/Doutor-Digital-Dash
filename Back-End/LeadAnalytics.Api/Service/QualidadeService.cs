using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.DTOs.Qualidade;
using LeadAnalytics.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace LeadAnalytics.Api.Service;

/// <summary>
/// Qualidade do preenchimento dos cartões, por campo e por responsável.
///
/// POR QUE NÃO É IA
/// ----------------
/// O erro que interessa é campo vazio ou incoerente, e isso é contável — não precisa de
/// julgamento. Pior: a IA não sabe qual era a verdade. Ela olharia um campo em branco e
/// chutaria, e aí o número errado passaria a ter cara de conferido, que é pior do que o
/// número errado assumido.
///
/// O QUE É "INCOERENTE"
/// --------------------
/// Só o que o próprio sistema consegue provar sem opinar: lead marcado como agendado sem
/// data de agendamento, tratamento fechado sem valor, perdido sem motivo. Nada aqui
/// depende de interpretar texto livre.
///
/// UMA REGRA É CORRIGÍVEL, AS OUTRAS NÃO
/// -------------------------------------
/// Só se corrige sozinho o que tem fonte melhor que a digitação: origem em branco num
/// lead que veio de anúncio rastreado — o rastreio sabe, o campo não foi preenchido. O
/// resto (motivo da perda, qualificação, valor) só existe na cabeça de quem atendeu, e
/// inventar valor ali seria fabricar dado.
/// </summary>
public class QualidadeService(AppDbContext db)
{
    private readonly AppDbContext _db = db;

    /// <summary>Etapas que significam "tem consulta marcada", nos dois funis.</summary>
    private static readonly string[] EtapasAgendado =
        [LeadStages.AgendadoSemPagamento, LeadStages.AgendadoComPagamento];

    /// <summary>Etapas em que o paciente já passou pela consulta.</summary>
    private static readonly string[] EtapasPosConsulta =
        [LeadStages.Compareceu, LeadStages.Negociacao, LeadStages.FechouTratamento,
         LeadStages.NaoFechouTratamento, LeadStages.EmTratamento];

    private static bool Vazio(string? s) =>
        string.IsNullOrWhiteSpace(s)
        || s.Trim().Equals("sem origem", StringComparison.OrdinalIgnoreCase)
        || s.Trim().Equals("n/a", StringComparison.OrdinalIgnoreCase);

    public async Task<QualidadeDto> GetAsync(
        int tenantId, int? unitId, DateTime de, DateTime ate, CancellationToken ct = default)
    {
        var leads = await _db.Leads.AsNoTracking()
            .Where(l => l.TenantId == tenantId
                        && (!unitId.HasValue || l.UnitId == unitId.Value)
                        && l.CreatedAt >= de && l.CreatedAt <= ate)
            .Select(l => new
            {
                l.Id, l.AttendantId, l.CurrentStage, l.Source, l.Campaign, l.Ad,
                l.Qualification, l.AppointmentScheduledAt, l.TreatmentPlanValue,
                l.ConsultationValue, l.NoAppointmentReason, l.LeadType,
            })
            .ToListAsync(ct);

        var total = leads.Count;
        if (total == 0)
            return new QualidadeDto { Total = 0, De = de, Ate = ate };

        // ── Preenchimento por campo ──────────────────────────────────────────
        // Só campos que alimentam KPI. Campo que ninguém usa não vira cobrança.
        var campos = new List<QualidadeCampoDto>
        {
            Campo("origem", "Origem", leads.Count(l => !Vazio(l.Source)), total),
            Campo("tipo_lead", "Tipo de lead", leads.Count(l => !Vazio(l.LeadType)), total),
            Campo("qualificacao", "Qualificação", leads.Count(l => !Vazio(l.Qualification)), total),
            Campo("responsavel", "Responsável", leads.Count(l => l.AttendantId != null), total),
            Campo("data_agendamento", "Data de agendamento", leads.Count(l => l.AppointmentScheduledAt != null), total),
            Campo("valor_tratamento", "Valor do tratamento", leads.Count(l => l.TreatmentPlanValue > 0), total),
            Campo("valor_consulta", "Valor da consulta", leads.Count(l => l.ConsultationValue > 0), total),
            Campo("motivo_perda", "Motivo do não agendamento", leads.Count(l => !Vazio(l.NoAppointmentReason)), total),
        };

        // ── Incoerências ─────────────────────────────────────────────────────
        // Cada regra devolve os leads que a violam, para o painel poder abrir a lista.
        var agendados = leads.Where(l => EtapasAgendado.Contains(l.CurrentStage ?? "")).ToList();
        var fechados = leads.Where(l => l.CurrentStage == LeadStages.FechouTratamento).ToList();
        var perdidos = leads.Where(l => l.CurrentStage == LeadStages.Perdido).ToList();
        var posConsulta = leads.Where(l => EtapasPosConsulta.Contains(l.CurrentStage ?? "")).ToList();

        var comRastreio = leads
            .Where(l => Vazio(l.Source) && (!Vazio(l.Campaign) || !Vazio(l.Ad)))
            .ToList();

        var regras = new List<QualidadeRegraDto>
        {
            Regra("origem_vazia_com_rastreio",
                "Origem em branco num lead que veio de anúncio rastreado",
                "O rastreio (campanha/anúncio) sabe de onde veio; o campo não foi preenchido. "
                + "É a única regra que dá para corrigir sozinha, porque existe fonte melhor que a digitação.",
                comRastreio.Select(l => l.Id), corrigivel: true),

            Regra("agendado_sem_data",
                "Marcado como agendado, mas sem data de agendamento",
                "Sem a data, o lead não entra em nenhum lembrete e o card de agendados não sabe de que dia ele é.",
                agendados.Where(l => l.AppointmentScheduledAt is null).Select(l => l.Id)),

            Regra("agendado_sem_qualificacao",
                "Agendado sem o termômetro (Quente/Morno/Frio)",
                "A qualificação é o que separa quem vale insistir de quem não vale.",
                agendados.Where(l => Vazio(l.Qualification)).Select(l => l.Id)),

            Regra("fechou_sem_valor",
                "Tratamento fechado sem valor lançado",
                "O faturamento do card sai daqui. Fechamento sem valor derruba o número sem ninguém perceber.",
                fechados.Where(l => !(l.TreatmentPlanValue > 0)).Select(l => l.Id)),

            Regra("perdido_sem_motivo",
                "Perdido sem motivo informado",
                "Sem motivo, não dá para saber o que atacar — é o campo que vira ação na semana seguinte.",
                perdidos.Where(l => Vazio(l.NoAppointmentReason)).Select(l => l.Id)),

            Regra("pos_consulta_sem_valor_consulta",
                "Passou pela consulta sem valor da consulta",
                "Vale para conferir o caixa do dia contra o que a recepção lançou.",
                posConsulta.Where(l => !(l.ConsultationValue > 0)).Select(l => l.Id)),

            Regra("sem_responsavel",
                "Sem responsável definido",
                "Lead sem dono não aparece em nenhum ranking e ninguém é cobrado por ele.",
                leads.Where(l => l.AttendantId is null).Select(l => l.Id)),
        };

        // ── Por responsável ──────────────────────────────────────────────────
        // O objetivo é conversa de gestão, não caça a culpado: conta quantos cartões
        // da pessoa têm alguma incoerência, não quantos "erros" ela cometeu.
        var idsComProblema = regras.SelectMany(r => r.LeadIds).ToHashSet();

        var nomes = await _db.Attendants.AsNoTracking()
            .Where(a => leads.Select(l => l.AttendantId).Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, a => a.Name, ct);

        var porResponsavel = leads
            .GroupBy(l => l.AttendantId)
            .Select(g => new QualidadeResponsavelDto
            {
                Responsavel = g.Key is int id && nomes.TryGetValue(id, out var n) ? n : "(sem responsável)",
                Total = g.Count(),
                ComIncoerencia = g.Count(l => idsComProblema.Contains(l.Id)),
            })
            .OrderByDescending(r => r.ComIncoerencia)
            .ToList();

        foreach (var r in porResponsavel)
            r.Percentual = r.Total == 0 ? 0 : Math.Round(100.0 * r.ComIncoerencia / r.Total, 1);

        return new QualidadeDto
        {
            Total = total,
            De = de,
            Ate = ate,
            LeadsComIncoerencia = idsComProblema.Count,
            PorCampo = campos.OrderBy(c => c.Percentual).ToList(),
            Regras = regras.Where(r => r.Quantidade > 0).OrderByDescending(r => r.Quantidade).ToList(),
            PorResponsavel = porResponsavel,
        };
    }

    private static QualidadeCampoDto Campo(string id, string rotulo, int preenchidos, int total) => new()
    {
        Campo = id,
        Rotulo = rotulo,
        Preenchidos = preenchidos,
        Vazios = total - preenchidos,
        Percentual = total == 0 ? 0 : Math.Round(100.0 * preenchidos / total, 1),
    };

    private static QualidadeRegraDto Regra(
        string id, string titulo, string porque, IEnumerable<int> ids, bool corrigivel = false)
    {
        var lista = ids.ToList();
        return new QualidadeRegraDto
        {
            Id = id,
            Titulo = titulo,
            Porque = porque,
            Quantidade = lista.Count,
            Corrigivel = corrigivel,
            // Teto para a resposta não virar dump: o painel abre a lista completa por
            // um endpoint próprio quando o usuário clica.
            LeadIds = lista.Take(500).ToList(),
        };
    }
}
