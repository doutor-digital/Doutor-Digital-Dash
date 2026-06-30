using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.DTOs.Juridico;
using LeadAnalytics.Api.Service.Insights;
using Microsoft.EntityFrameworkCore;

namespace LeadAnalytics.Api.Service.Juridico;

/// <summary>
/// Calcula o dashboard do segmento jurídico. Carrega os leads do tenant/período uma vez e
/// deriva os 7 grupos de métricas em memória, lendo os custom fields via
/// <see cref="JuridicoFieldMap"/> (mapeado por unidade nas Configurações Técnicas). Métricas
/// de I.A. vêm de <see cref="Models.AgentConversation"/>; SLA reaproveita <see cref="InsightsService"/>;
/// investimento do ROI vem de <see cref="Models.CampaignDailySpend"/>.
///
/// Quando um papel de campo não está mapeado, a métrica correspondente fica zerada e o
/// papel é listado em <see cref="JuridicoDashboardDto.CamposNaoMapeados"/>.
/// </summary>
public sealed class JuridicoDashboardService(
    AppDbContext db,
    JuridicoFieldMapService fieldMapService,
    InsightsService insights)
{
    public async Task<JuridicoDashboardDto> BuildAsync(
        int clinicId, int? unitId, DateTime from, DateTime to, CancellationToken ct = default)
    {
        from = AsUtc(from);
        to = AsUtc(to);

        // O mapa de campos é por unidade; quando "Todas as unidades", usa a primeira unidade do tenant.
        var mapUnitId = unitId ?? await db.Units.AsNoTracking()
            .Where(u => u.ClinicId == clinicId)
            .OrderBy(u => u.Id)
            .Select(u => (int?)u.Id)
            .FirstOrDefaultAsync(ct);

        var map = mapUnitId is null
            ? new JuridicoFieldMap()
            : await fieldMapService.LoadAsync(mapUnitId.Value, ct);

        var leadsQuery = db.Leads.AsNoTracking().ExcludeDeleted()
            .Where(l => l.TenantId == clinicId);
        if (unitId.HasValue) leadsQuery = leadsQuery.Where(l => l.UnitId == unitId.Value);

        var rows = await leadsQuery
            .Select(l => new LeadRow
            {
                CustomFieldsJson = l.CustomFieldsJson,
                CurrentStage = l.CurrentStage,
                AttendanceStatus = l.AttendanceStatus,
                HasAppointment = l.HasAppointment,
                HasPayment = l.HasPayment,
                AppointmentScheduledAt = l.AppointmentScheduledAt,
                ClosedTreatment = l.ClosedTreatment,
                Price = l.Price,
                Ad = l.Ad,
                Campaign = l.Campaign,
                Source = l.Source,
                CreatedAt = l.CreatedAt,
                OriginalCreatedAt = l.OriginalCreatedAt,
            })
            .ToListAsync(ct);

        // Filtra por data de criação real (OriginalCreatedAt ?? CreatedAt) dentro do período.
        var leads = rows
            .Where(r => { var d = r.OriginalCreatedAt ?? r.CreatedAt; return d >= from && d <= to; })
            .Select(r => Project(r, map))
            .ToList();

        var dto = new JuridicoDashboardDto
        {
            From = from,
            To = to,
            TotalLeads = leads.Count,
            CamposNaoMapeados = UnmappedFields(map),
        };

        dto.AreaCaso = BuildAreaCaso(leads);
        dto.Secretarias = BuildSecretarias(leads);
        dto.Conversao = BuildConversao(leads);
        dto.Qualificacao = BuildQualificacao(leads);
        dto.Roi = await BuildRoiAsync(clinicId, leads, from, to, ct);
        dto.Ia = await BuildIaAsync(clinicId, unitId, leads, from, to, ct);
        dto.Sla = await BuildSlaAsync(clinicId, unitId, leads, from, to, ct);

        return dto;
    }

    // ─── Projeção: lead cru → flags de negócio ────────────────────────────────
    private static LeadProjected Project(LeadRow r, JuridicoFieldMap m)
    {
        var cf = r.CustomFieldsJson;

        var area = CustomFieldReader.Read(cf, m.AreaCasoFieldId, n => n.Contains("área") || n.Contains("area") || n.Contains("tipo de caso"))
                   ?.Trim();
        var responsavel = CustomFieldReader.Read(cf, m.ResponsavelFieldId, n => n.Contains("respons") || n.Contains("secret") || n.Contains("atendente"))
                          ?.Trim();
        var criativo = CustomFieldReader.Read(cf, m.TrackingIdFieldId, n => n.Contains("tracking") || n.Contains("criativo") || n.Contains("anúncio") || n.Contains("anuncio"))
                       ?.Trim();
        var qualificadoPor = CustomFieldReader.Read(cf, m.QualificadoPorFieldId, n => n.Contains("qualificado por"))?.Trim();
        var agendadoPor = CustomFieldReader.Read(cf, m.AgendadoPorFieldId, n => n.Contains("agendado por"))?.Trim();
        var statusQualif = CustomFieldReader.Read(cf, m.StatusQualificacaoFieldId, n => n.Contains("qualifica"))?.Trim();
        var motivoPerda = CustomFieldReader.Read(cf, m.MotivoPerdaFieldId, n => n.Contains("motivo") && (n.Contains("perda") || n.Contains("perd")))?.Trim();
        var motivoDesq = CustomFieldReader.Read(cf, m.MotivoDesqualificacaoFieldId, n => n.Contains("motivo") && n.Contains("desqualif"))?.Trim();
        var grupo = CustomFieldReader.Read(cf, m.GrupoFieldId, n => n.Contains("grupo") || n.Contains("equipe"))?.Trim();

        var valorEstimado = CustomFieldReader.ReadDecimal(cf, m.ValorEstimadoFieldId, n => n.Contains("valor") && n.Contains("estimad"))
                            ?? r.Price ?? 0m;
        var honorario = CustomFieldReader.ReadDecimal(cf, m.HonorarioExitoFieldId, n => n.Contains("honor") && (n.Contains("êxito") || n.Contains("exito"))) ?? 0m;

        var isAgendado = r.HasAppointment || r.AppointmentScheduledAt is not null || LeadStages.IsScheduled(r.CurrentStage);
        var compareceu = string.Equals(r.AttendanceStatus, LeadStages.AttendedCompareceu, StringComparison.OrdinalIgnoreCase)
                         || r.CurrentStage == LeadStages.FechouTratamento || r.CurrentStage == LeadStages.EmTratamento;
        var contratoField = CustomFieldReader.Matches(cf, m.ContratoAssinadoFieldId, ContractYes, n => n.Contains("contrato"));
        var isContrato = contratoField || r.HasPayment || r.ClosedTreatment == true || r.CurrentStage == LeadStages.FechouTratamento;

        var isDesqualificado = !string.IsNullOrEmpty(statusQualif) && statusQualif!.Contains("desqualif", StringComparison.OrdinalIgnoreCase);
        var isQualificado = !isDesqualificado && (
            (!string.IsNullOrEmpty(statusQualif) && statusQualif!.Contains("qualific", StringComparison.OrdinalIgnoreCase))
            || !string.IsNullOrEmpty(qualificadoPor)
            || isAgendado);

        var iaQualificou = !string.IsNullOrEmpty(qualificadoPor) && qualificadoPor!.Contains("ia", StringComparison.OrdinalIgnoreCase);
        var iaAgendou = !string.IsNullOrEmpty(agendadoPor) && agendadoPor!.Contains("ia", StringComparison.OrdinalIgnoreCase);

        return new LeadProjected
        {
            Area = string.IsNullOrEmpty(area) ? "Não informado" : area!,
            Responsavel = string.IsNullOrEmpty(responsavel) ? "Não atribuído" : responsavel!,
            Criativo = !string.IsNullOrEmpty(criativo) ? criativo!
                       : !string.IsNullOrEmpty(r.Ad) ? r.Ad!
                       : !string.IsNullOrEmpty(r.Campaign) && r.Campaign != "DESCONHECIDO" ? r.Campaign!
                       : "Não informado",
            Grupo = string.IsNullOrEmpty(grupo) ? "Não informado" : grupo!,
            MotivoPerda = string.IsNullOrEmpty(motivoPerda) ? null : motivoPerda,
            MotivoDesqualificacao = string.IsNullOrEmpty(motivoDesq) ? null : motivoDesq,
            ValorEstimado = valorEstimado,
            HonorarioExito = honorario,
            IsAgendado = isAgendado,
            Compareceu = compareceu,
            IsContrato = isContrato,
            IsQualificado = isQualificado,
            IsDesqualificado = isDesqualificado,
            IaQualificou = iaQualificou,
            IaAgendou = iaAgendou,
        };
    }

    // ─── 1. Área de cada lead ─────────────────────────────────────────────────
    private static List<AreaCasoDto> BuildAreaCaso(List<LeadProjected> leads)
    {
        var total = leads.Count;
        return leads.GroupBy(l => l.Area)
            .Select(g => new AreaCasoDto
            {
                Area = g.Key,
                Leads = g.Count(),
                Pct = total > 0 ? Math.Round(g.Count() * 100.0 / total, 1) : 0,
                PorCriativo = g.GroupBy(x => x.Criativo)
                    .Select(c => new LabeledCountDto { Label = c.Key, Count = c.Count() })
                    .OrderByDescending(c => c.Count).Take(10).ToList(),
            })
            .OrderByDescending(a => a.Leads)
            .ToList();
    }

    // ─── 2. Qualidade de vendas das secretárias ───────────────────────────────
    private static List<SecretariaDto> BuildSecretarias(List<LeadProjected> leads)
    {
        return leads.GroupBy(l => l.Responsavel)
            .Select(g =>
            {
                var leadsN = g.Count();
                var agendados = g.Count(x => x.IsAgendado);
                var compareceram = g.Count(x => x.Compareceu);
                var noShow = g.Count(x => x.IsAgendado && !x.Compareceu);
                var contratos = g.Count(x => x.IsContrato);
                var perdas = g.Count(x => x.IsDesqualificado || x.MotivoPerda is not null);
                var principalPerda = g.Where(x => x.MotivoPerda is not null)
                    .GroupBy(x => x.MotivoPerda!)
                    .OrderByDescending(p => p.Count())
                    .Select(p => p.Key).FirstOrDefault();

                return new SecretariaDto
                {
                    Nome = g.Key,
                    Leads = leadsN,
                    Agendados = agendados,
                    TaxaAgendamento = Pct(agendados, leadsN),
                    Compareceram = compareceram,
                    NoShow = noShow,
                    TaxaNoShow = Pct(noShow, agendados),
                    Contratos = contratos,
                    TaxaFechamento = Pct(contratos, compareceram),
                    Perdas = perdas,
                    PrincipalMotivoPerda = principalPerda,
                };
            })
            .OrderByDescending(s => s.Leads)
            .ToList();
    }

    // ─── 5. Conversão (funil) ─────────────────────────────────────────────────
    private static ConversaoJuridicaDto BuildConversao(List<LeadProjected> leads)
    {
        int lead = leads.Count;
        int qualificado = leads.Count(l => l.IsQualificado);
        int agendado = leads.Count(l => l.IsAgendado);
        int compareceu = leads.Count(l => l.Compareceu);
        int contrato = leads.Count(l => l.IsContrato);

        var dto = new ConversaoJuridicaDto
        {
            Lead = lead,
            Qualificado = qualificado,
            Agendado = agendado,
            Compareceu = compareceu,
            Contrato = contrato,
            TaxaQualificacao = Pct(qualificado, lead),
            TaxaAgendamento = Pct(agendado, qualificado),
            TaxaComparecimento = Pct(compareceu, agendado),
            TaxaFechamento = Pct(contrato, compareceu),
            TaxaGeral = Pct(contrato, lead),
        };

        // Gargalo = etapa com a maior queda percentual entre estágios consecutivos.
        var steps = new (string Label, double Rate)[]
        {
            ("Lead → Qualificado", dto.TaxaQualificacao),
            ("Qualificado → Agendado", dto.TaxaAgendamento),
            ("Agendado → Compareceu", dto.TaxaComparecimento),
            ("Compareceu → Contrato", dto.TaxaFechamento),
        };
        var withData = steps.Where(s => s.Rate > 0).ToList();
        dto.Gargalo = withData.Count > 0
            ? withData.OrderBy(s => s.Rate).First().Label
            : null;

        return dto;
    }

    // ─── 6. Qualificado × Desqualificado ──────────────────────────────────────
    private static QualificacaoDto BuildQualificacao(List<LeadProjected> leads)
    {
        var qualificados = leads.Count(l => l.IsQualificado);
        var desqualificados = leads.Count(l => l.IsDesqualificado);
        var baseQ = qualificados + desqualificados;

        return new QualificacaoDto
        {
            Qualificados = qualificados,
            Desqualificados = desqualificados,
            TaxaQualificacao = Pct(qualificados, baseQ),
            MotivosDesqualificacao = leads.Where(l => l.MotivoDesqualificacao is not null)
                .GroupBy(l => l.MotivoDesqualificacao!)
                .Select(g => new LabeledCountDto { Label = g.Key, Count = g.Count() })
                .OrderByDescending(m => m.Count).ToList(),
            PorCriativo = leads.GroupBy(l => l.Criativo)
                .Select(g =>
                {
                    var q = g.Count(x => x.IsQualificado);
                    var d = g.Count(x => x.IsDesqualificado);
                    return new CriativoQualificacaoDto
                    {
                        Criativo = g.Key,
                        Leads = g.Count(),
                        Qualificados = q,
                        Desqualificados = d,
                        TaxaQualificacao = Pct(q, q + d),
                    };
                })
                .OrderByDescending(c => c.Leads).ToList(),
        };
    }

    // ─── 7. Receita real / ROI por criativo ───────────────────────────────────
    private async Task<RoiJuridicoDto> BuildRoiAsync(
        int clinicId, List<LeadProjected> leads, DateTime from, DateTime to, CancellationToken ct)
    {
        var fromDate = DateOnly.FromDateTime(from);
        var toDate = DateOnly.FromDateTime(to);
        var spend = await db.CampaignDailySpends.AsNoTracking()
            .Where(s => s.ClinicId == clinicId && s.Date >= fromDate && s.Date <= toDate)
            .Select(s => new { s.CampaignName, s.Spend })
            .ToListAsync(ct);

        var investimentoTotal = spend.Sum(s => s.Spend);

        var porCriativo = leads.GroupBy(l => l.Criativo)
            .Select(g =>
            {
                var leadsN = g.Count();
                // Investimento atribuído: melhor esforço por nome de campanha contendo o criativo.
                var inv = spend
                    .Where(s => !string.IsNullOrEmpty(s.CampaignName)
                                && (s.CampaignName!.Contains(g.Key, StringComparison.OrdinalIgnoreCase)
                                    || g.Key.Contains(s.CampaignName!, StringComparison.OrdinalIgnoreCase)))
                    .Sum(s => s.Spend);
                var honorario = g.Sum(x => x.HonorarioExito);
                return new RoiCriativoDto
                {
                    Criativo = g.Key,
                    Leads = leadsN,
                    Contratos = g.Count(x => x.IsContrato),
                    ValorEstimado = g.Sum(x => x.ValorEstimado),
                    HonorarioExito = honorario,
                    Investimento = inv,
                    Roi = inv > 0 ? (double)(honorario / inv) : null,
                    CustoPorLead = inv > 0 && leadsN > 0 ? inv / leadsN : null,
                };
            })
            .OrderByDescending(c => c.HonorarioExito).ToList();

        var porArea = leads.GroupBy(l => l.Area)
            .Select(g => new ValorAreaDto
            {
                Area = g.Key,
                ValorEstimado = g.Sum(x => x.ValorEstimado),
                HonorarioExito = g.Sum(x => x.HonorarioExito),
                Contratos = g.Count(x => x.IsContrato),
            })
            .OrderByDescending(a => a.HonorarioExito).ToList();

        var honorarioTotal = leads.Sum(l => l.HonorarioExito);
        return new RoiJuridicoDto
        {
            PorCriativo = porCriativo,
            PorArea = porArea,
            ValorEstimadoTotal = leads.Sum(l => l.ValorEstimado),
            HonorarioExitoTotal = honorarioTotal,
            InvestimentoTotal = investimentoTotal,
            RoiGeral = investimentoTotal > 0 ? (double)(honorarioTotal / investimentoTotal) : null,
        };
    }

    // ─── 3. Qualidade da I.A. ─────────────────────────────────────────────────
    private async Task<IaQualidadeDto> BuildIaAsync(
        int clinicId, int? unitId, List<LeadProjected> leads, DateTime from, DateTime to, CancellationToken ct)
    {
        var convQuery = db.AgentConversations.AsNoTracking()
            .Where(c => c.TenantId == clinicId && c.StartedAt >= from && c.StartedAt <= to);
        if (unitId.HasValue) convQuery = convQuery.Where(c => c.UnitId == unitId.Value);

        var conversas = await convQuery
            .Select(c => new { c.HandedOff })
            .ToListAsync(ct);

        var atendidos = conversas.Count;
        var handoffs = conversas.Count(c => c.HandedOff);

        var qualificados = leads.Count(l => l.IaQualificou);
        var agendadosIa = leads.Count(l => l.IaAgendou && l.IsAgendado);
        var contribui = leads.Count(l => l.IaQualificou && l.IsContrato);

        return new IaQualidadeDto
        {
            LeadsAtendidos = atendidos,
            Qualificados = qualificados,
            TaxaQualificacao = Pct(qualificados, leads.Count),
            AgendadosPelaIa = agendadosIa,
            TaxaIaAgendamento = Pct(agendadosIa, qualificados),
            Handoffs = handoffs,
            TaxaHandoff = Pct(handoffs, atendidos),
            ContribuiContratos = contribui,
            PrincipalPerda = leads.Where(l => l.IaQualificou && l.MotivoPerda is not null)
                .GroupBy(l => l.MotivoPerda!)
                .OrderByDescending(g => g.Count())
                .Select(g => g.Key).FirstOrDefault(),
        };
    }

    // ─── 4. SLA de 1ª resposta ────────────────────────────────────────────────
    private async Task<SlaJuridicoDto> BuildSlaAsync(
        int clinicId, int? unitId, List<LeadProjected> leads, DateTime from, DateTime to, CancellationToken ct)
    {
        var sla = await insights.GetSlaAsync(clinicId, unitId, from, to, targetMinutes: 5, ct);
        return new SlaJuridicoDto
        {
            MediaMinutos = sla.AverageFirstResponseMinutes,
            MedianaMinutos = sla.MedianFirstResponseMinutes,
            P90Minutos = sla.P90FirstResponseMinutes,
            LeadsComResposta = sla.LeadsWithFirstResponse,
            // IA × humano e por grupo dependem de dados ainda não modelados de forma granular;
            // ficam para um próximo incremento (atributo do interlocutor por interação).
        };
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────
    private static readonly string[] ContractYes = { "sim", "yes", "true", "assinado", "fechado" };

    private static double Pct(int part, int whole) => whole > 0 ? Math.Round(part * 100.0 / whole, 1) : 0;

    private static List<string> UnmappedFields(JuridicoFieldMap m)
    {
        var missing = new List<string>();
        void Check(long? id, string label) { if (id is null) missing.Add(label); }
        Check(m.AreaCasoFieldId, "Área do caso");
        Check(m.QualificadoPorFieldId, "Qualificado por");
        Check(m.ResponsavelFieldId, "Responsável (secretária)");
        Check(m.StatusQualificacaoFieldId, "Status de qualificação");
        Check(m.MotivoDesqualificacaoFieldId, "Motivo de desqualificação");
        Check(m.ValorEstimadoFieldId, "Valor estimado");
        Check(m.HonorarioExitoFieldId, "Honorário de êxito");
        Check(m.TrackingIdFieldId, "tracking_id (criativo)");
        return missing;
    }

    private static DateTime AsUtc(DateTime d) =>
        d.Kind == DateTimeKind.Utc ? d
        : d.Kind == DateTimeKind.Local ? d.ToUniversalTime()
        : DateTime.SpecifyKind(d, DateTimeKind.Utc);

    // Projeção crua do banco (antes de derivar flags).
    private sealed class LeadRow
    {
        public string? CustomFieldsJson { get; set; }
        public string CurrentStage { get; set; } = "";
        public string? AttendanceStatus { get; set; }
        public bool HasAppointment { get; set; }
        public bool HasPayment { get; set; }
        public DateTime? AppointmentScheduledAt { get; set; }
        public bool? ClosedTreatment { get; set; }
        public decimal? Price { get; set; }
        public string? Ad { get; set; }
        public string? Campaign { get; set; }
        public string? Source { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? OriginalCreatedAt { get; set; }
    }

    private sealed class LeadProjected
    {
        public string Area { get; set; } = "";
        public string Responsavel { get; set; } = "";
        public string Criativo { get; set; } = "";
        public string Grupo { get; set; } = "";
        public string? MotivoPerda { get; set; }
        public string? MotivoDesqualificacao { get; set; }
        public decimal ValorEstimado { get; set; }
        public decimal HonorarioExito { get; set; }
        public bool IsAgendado { get; set; }
        public bool Compareceu { get; set; }
        public bool IsContrato { get; set; }
        public bool IsQualificado { get; set; }
        public bool IsDesqualificado { get; set; }
        public bool IaQualificou { get; set; }
        public bool IaAgendou { get; set; }
    }
}
