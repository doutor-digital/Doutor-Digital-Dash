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

        int Preenchidos(long? fieldId, Func<dynamic, string?>? coluna = null) =>
            fieldId is null ? -1 : leads.Count(l => !Vazio(Valor(l, fieldId, coluna?.Invoke(l))));

        var campos = new List<QualidadeCampoDto>();
        void Campo(string id, string rotulo, long? fieldId, Func<dynamic, string?>? coluna = null)
        {
            var p = Preenchidos(fieldId, coluna);
            // -1 = campo sem mapeamento. Aparece como pendência de configuração, não
            // como falha de preenchimento — misturar os dois foi o que gerou a cobrança
            // injusta da primeira versão.
            campos.Add(new QualidadeCampoDto
            {
                Campo = id,
                Rotulo = rotulo,
                Mapeado = p >= 0,
                Preenchidos = Math.Max(p, 0),
                Vazios = p < 0 ? 0 : total - p,
                Percentual = p < 0 ? 0 : Math.Round(100.0 * p / total, 1),
            });
        }

        // NÃO usar Lead.Source como reforço: ela vale "Kommo" em 100% dos leads — é a
        // origem do SISTEMA, não a de marketing. Usá-la faria a Origem aparecer sempre
        // como 100% preenchida, inclusive nos 523 leads que estão em "Sem origem".
        Campo("origem", "Origem", mapa.OrigemFieldId);
        Campo("qualificacao", "Qualificação", mapa.QualificacaoFieldId, l => (string?)l.Qualification);
        Campo("tipo_lead", "Tipo de lead", mapa.TipoFieldId);
        Campo("motivo_nao_agendamento", "Motivo do não agendamento", mapa.MotivoNaoAgendamentoFieldId);
        Campo("tipo_agendamento", "Tipo de agendamento", mapa.TipoAgendamentoFieldId);
        Campo("fisioterapeuta", "Fisioterapeuta", mapa.FisioterapeutaFieldId);
        Campo("valor_tratamento", "Valor do tratamento", mapa.ValorTratamentoFieldId);
        Campo("valor_consulta", "Valor da consulta", mapa.ValorConsultaFieldId);
        Campo("tratamento_fechado", "Fechou tratamento", mapa.TratamentoFechadoFieldId);
        Campo("data_agendamento", "Data de agendamento", mapa.AppointmentFieldId);

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
