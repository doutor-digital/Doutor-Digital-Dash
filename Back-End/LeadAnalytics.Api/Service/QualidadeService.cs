using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.DTOs.Qualidade;
using Microsoft.EntityFrameworkCore;

namespace LeadAnalytics.Api.Service;

/// <summary>
/// Qualidade do preenchimento dos cartões, por campo e por responsável.
///
/// LÊ DO CustomFieldsJson, E ISSO NÃO É DETALHE
/// --------------------------------------------
/// As colunas tipadas do lead (LeadType, NoAppointmentReason, TreatmentPlanValue) ficam
/// vazias por desenho: o sync grava o JSON dos campos customizados e o cálculo resolve na
/// consulta. A primeira versão deste serviço media a coluna e acusava 0% em campos que
/// estão preenchidos em 87% — o tipo de erro que faz a gestão cobrar a equipe por um
/// defeito nosso. Agora lê o JSON, com a coluna tipada como reforço quando existe.
///
/// QUAIS CAMPOS ENTRAM
/// -------------------
/// Só os que a unidade mapeou em Configurações Técnicas. Campo não mapeado não vira
/// cobrança — se ninguém disse onde ele está, a ausência é da configuração, não de quem
/// preenche.
///
/// POR QUE NÃO É IA
/// ----------------
/// O erro que interessa é campo vazio ou incoerente, e isso é contável. A IA não sabe qual
/// era a verdade: olharia o campo em branco e chutaria, e aí o número errado passaria a ter
/// cara de conferido — pior do que o número errado assumido.
/// </summary>
public class QualidadeService(AppDbContext db, KpiConfigService kpiConfig)
{
    private readonly AppDbContext _db = db;
    private readonly KpiConfigService _kpiConfig = kpiConfig;

    /// <summary>Abaixo disso o campo é cobrado. Acordado com a operação.</summary>
    public const double MetaPercentual = 90;

    private static readonly string[] EtapasAgendado =
        [LeadStages.AgendadoSemPagamento, LeadStages.AgendadoComPagamento];

    /// <summary>
    /// Posição da etapa no funil. O denominador de um campo é "quantos leads CHEGARAM na
    /// etapa em que ele passa a ser exigido" — medir contra a base inteira faz um campo
    /// obrigatório só no AGENDADO aparecer com 8%, quando a maioria dos leads nunca saiu
    /// da qualificação. O número fica baixo, ninguém entende, e o painel perde a
    /// autoridade que deveria ter.
    /// </summary>
    private static int Rank(string? etapa) => etapa switch
    {
        LeadStages.Qualificacao => 1,
        LeadStages.AgendadoSemPagamento or LeadStages.AgendadoComPagamento => 2,
        LeadStages.Compareceu or LeadStages.Faltou => 3,
        LeadStages.Negociacao or LeadStages.NaoFechouTratamento => 4,
        LeadStages.FechouTratamento or LeadStages.EmTratamento or LeadStages.Alta => 5,
        _ => 0,
    };

    /// <summary>Etapa a partir da qual o campo é exigido — espelha required_statuses da Kommo.</summary>
    private const int ExigeQualificacao = 1;
    private const int ExigeAgendado = 2;
    private const int ExigeNegociacao = 4;
    private const int ExigeGanho = 5;

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
                l.Id, l.AttendantId, l.CurrentStage, l.CustomFieldsJson,
                l.Source, l.Qualification, l.AppointmentScheduledAt, l.TreatmentPlanValue,
            })
            .ToListAsync(ct);

        var total = leads.Count;
        if (total == 0) return new QualidadeDto { Total = 0, De = de, Ate = ate, Meta = MetaPercentual };

        // O mapa de qual campo da Kommo é o quê, por unidade. Sem unidade não há mapa —
        // cada uma tem os seus ids —, então o painel só mede campo quando há unidade.
        var mapa = unitId.HasValue
            ? await _kpiConfig.GetLeadProfileConfigAsync(unitId.Value, ct)
            : new KpiConfigService.LeadProfileFields();

        /// Valor do campo: JSON primeiro, coluna tipada como reforço.
        string? Valor(dynamic l, long? fieldId, string? coluna = null)
        {
            if (!string.IsNullOrWhiteSpace(coluna)) return coluna;
            if (fieldId is null) return null;
            var json = (string?)l.CustomFieldsJson;
            return string.IsNullOrWhiteSpace(json)
                ? null
                : KpiConfigService.ExtractFieldValue(json, fieldId, null);
        }

        var campos = new List<QualidadeCampoDto>();

        /// <param name="exigeApartirDe">Rank da etapa em que o campo passa a ser exigido.</param>
        void Campo(string id, string rotulo, long? fieldId, int exigeApartirDe,
                   string etapaRotulo, Func<dynamic, string?>? coluna = null)
        {
            // Só entram no denominador os leads que CHEGARAM na etapa. Lead que parou
            // na qualificação não deve nada de campo do agendamento.
            var universo = leads.Where(l => Rank((string?)l.CurrentStage) >= exigeApartirDe).ToList();

            var mapeado = fieldId is not null;
            var p = mapeado ? universo.Count(l => !Vazio(Valor(l, fieldId, coluna?.Invoke(l)))) : 0;

            campos.Add(new QualidadeCampoDto
            {
                Campo = id,
                Rotulo = rotulo,
                Mapeado = mapeado,
                Etapa = etapaRotulo,
                Universo = universo.Count,
                Preenchidos = p,
                Vazios = mapeado ? universo.Count - p : 0,
                Percentual = !mapeado || universo.Count == 0 ? 0 : Math.Round(100.0 * p / universo.Count, 1),
            });
        }

        // A etapa de cada campo espelha o required_statuses declarado na própria Kommo:
        // é ela quem sabe onde o campo passa a ser obrigatório, não a gente.
        //
        // NÃO usar Lead.Source como reforço da Origem: vale "Kommo" em 100% dos leads —
        // é a origem do SISTEMA, não a de marketing.
        Campo("origem", "Origem", mapa.OrigemFieldId, ExigeQualificacao, "a partir de Em qualificação");
        Campo("tipo_lead", "Tipo de lead", mapa.TipoFieldId, ExigeQualificacao, "a partir de Em qualificação");

        Campo("qualificacao", "Qualificação", mapa.QualificacaoFieldId, ExigeAgendado,
            "a partir de Agendado", l => (string?)l.Qualification);
        Campo("data_agendamento", "Data de agendamento", mapa.AppointmentFieldId, ExigeAgendado,
            "a partir de Agendado");
        Campo("tipo_agendamento", "Tipo de agendamento", mapa.TipoAgendamentoFieldId, ExigeAgendado,
            "a partir de Agendado");

        Campo("valor_tratamento", "Valor do tratamento", mapa.ValorTratamentoFieldId, ExigeNegociacao,
            "a partir de Em negociação");

        Campo("fisioterapeuta", "Fisioterapeuta", mapa.FisioterapeutaFieldId, ExigeGanho, "no Ganho");
        Campo("tratamento_fechado", "Fechou tratamento", mapa.TratamentoFechadoFieldId, ExigeGanho, "no Ganho");
        Campo("valor_consulta", "Valor da consulta", mapa.ValorConsultaFieldId, ExigeGanho, "no Ganho");

        // Motivo é o único que não segue o funil: só faz sentido em quem foi perdido.
        {
            var perdidosUniverso = leads.Where(l => (string?)l.CurrentStage == LeadStages.Perdido).ToList();
            var mapeadoMotivo = mapa.MotivoNaoAgendamentoFieldId is not null;
            var pm = mapeadoMotivo
                ? perdidosUniverso.Count(l => !Vazio(Valor(l, mapa.MotivoNaoAgendamentoFieldId)))
                : 0;
            campos.Add(new QualidadeCampoDto
            {
                Campo = "motivo_nao_agendamento",
                Rotulo = "Motivo do não agendamento",
                Mapeado = mapeadoMotivo,
                Etapa = "apenas em Perdido",
                Universo = perdidosUniverso.Count,
                Preenchidos = pm,
                Vazios = mapeadoMotivo ? perdidosUniverso.Count - pm : 0,
                Percentual = !mapeadoMotivo || perdidosUniverso.Count == 0
                    ? 0 : Math.Round(100.0 * pm / perdidosUniverso.Count, 1),
            });
        }

        foreach (var c in campos) c.AtingiuMeta = c.Mapeado && c.Percentual >= MetaPercentual;

        // ── Incoerências: só o que o sistema prova sem opinar ────────────────
        var agendados = leads.Where(l => EtapasAgendado.Contains(l.CurrentStage ?? "")).ToList();
        var fechados = leads.Where(l => l.CurrentStage == LeadStages.FechouTratamento).ToList();
        var perdidos = leads.Where(l => l.CurrentStage == LeadStages.Perdido).ToList();
        var posConsulta = leads.Where(l => EtapasPosConsulta.Contains(l.CurrentStage ?? "")).ToList();

        var regras = new List<QualidadeRegraDto>
        {
            Regra("agendado_sem_data",
                "Marcado como agendado, mas sem data de agendamento",
                "Sem a data, o lead não entra em lembrete e o card do dia não sabe de que dia ele é.",
                agendados.Where(l => l.AppointmentScheduledAt is null
                                     && Vazio(Valor(l, mapa.AppointmentFieldId))).Select(l => (int)l.Id)),

            Regra("perdido_sem_motivo",
                "Perdido sem motivo informado",
                "Sem motivo não há o que atacar na semana seguinte — é o campo que vira ação.",
                perdidos.Where(l => Vazio(Valor(l, mapa.MotivoNaoAgendamentoFieldId))).Select(l => (int)l.Id)),

            Regra("fechou_sem_valor",
                "Tratamento fechado sem valor lançado",
                "O faturamento do card sai daqui; fechamento sem valor derruba o número em silêncio.",
                fechados.Where(l => !(l.TreatmentPlanValue > 0)
                                    && Vazio(Valor(l, mapa.ValorTratamentoFieldId))).Select(l => (int)l.Id)),

            Regra("pos_consulta_sem_valor_consulta",
                "Passou pela consulta sem valor da consulta",
                "É o que permite conferir o caixa do dia contra o que foi lançado.",
                posConsulta.Where(l => Vazio(Valor(l, mapa.ValorConsultaFieldId))).Select(l => (int)l.Id)),

            Regra("sem_responsavel",
                "Sem responsável definido",
                "Lead sem dono não aparece em ranking e ninguém é cobrado por ele.",
                leads.Where(l => l.AttendantId is null).Select(l => (int)l.Id)),
        };

        var idsComProblema = regras.SelectMany(r => r.LeadIds).ToHashSet();

        var nomes = await _db.Attendants.AsNoTracking()
            .Where(a => leads.Select(l => l.AttendantId).Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, a => a.Name, ct);

        var porResponsavel = leads
            .GroupBy(l => (int?)l.AttendantId)
            .Select(g => new QualidadeResponsavelDto
            {
                Responsavel = g.Key is int id && nomes.TryGetValue(id, out var n) ? n : "(sem responsável)",
                Total = g.Count(),
                ComIncoerencia = g.Count(l => idsComProblema.Contains((int)l.Id)),
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
            Meta = MetaPercentual,
            LeadsComIncoerencia = idsComProblema.Count,
            CamposAbaixoDaMeta = campos.Count(c => c.Mapeado && !c.AtingiuMeta),
            CamposSemMapeamento = campos.Count(c => !c.Mapeado),
            PorCampo = campos.OrderBy(c => c.Mapeado).ThenBy(c => c.Percentual).ToList(),
            Regras = regras.Where(r => r.Quantidade > 0).OrderByDescending(r => r.Quantidade).ToList(),
            PorResponsavel = porResponsavel,
        };
    }

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
            LeadIds = lista.Take(500).ToList(),
        };
    }
}
