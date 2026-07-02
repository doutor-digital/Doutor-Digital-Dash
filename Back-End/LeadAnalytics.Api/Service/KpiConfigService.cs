using System.Globalization;
using System.Text.Json;
using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace LeadAnalytics.Api.Service;

/// <summary>
/// Lê/grava o mapeamento de KPIs por unidade e — o coração da feature — calcula o
/// número de um KPI a partir da sua fonte configurada (etapa da Kommo, campo
/// customizado, ou filtro combinado), tudo direto do nosso banco
/// (Lead.CurrentStageId + Lead.CustomFieldsJson), sem bater na Kommo ao vivo.
/// </summary>
public class KpiConfigService(AppDbContext db)
{
    private readonly AppDbContext _db = db;

    // Chaves (em kpi_configurations) do mapeamento de campos do Perfil do Lead por unidade.
    public const string ProfileBirthdateKey = "profile_birthdate";
    public const string ProfileAppointmentKey = "profile_appointment_date";
    public const string ProfileDoctorKey = "profile_doctor";
    public const string ProfileOrigemKey = "profile_origem";
    public const string ProfileMotivoKey = "profile_motivo_nao_agendamento";
    public const string ProfileFisioKey = "profile_fisioterapeuta";
    public const string ProfileValorTratKey = "profile_valor_tratamento";
    public const string ProfileValorConsultaKey = "profile_valor_consulta";
    public const string ProfileTratFechadoKey = "profile_tratamento_fechado";
    public const string ProfileQualificacaoKey = "profile_qualificacao";
    public const string ProfileTipoKey = "profile_tipo";
    public const string ProfileTipoAgendamentoKey = "profile_tipo_agendamento";
    public const string ProfileTipoTratamentoKey = "profile_tipo_tratamento";

    /// <summary>Mapeamento dos custom fields da Kommo p/ os breakdowns do dashboard.</summary>
    public class LeadProfileFields
    {
        public long? BirthdateFieldId { get; set; }
        public long? AppointmentFieldId { get; set; }
        public long? DoctorFieldId { get; set; }
        public long? OrigemFieldId { get; set; }
        public long? MotivoNaoAgendamentoFieldId { get; set; }
        public long? FisioterapeutaFieldId { get; set; }
        public long? ValorTratamentoFieldId { get; set; }
        /// <summary>Campo "Valor da consulta" da Kommo — alimenta o card Consultas.ValorTotal.</summary>
        public long? ValorConsultaFieldId { get; set; }
        public long? TratamentoFechadoFieldId { get; set; }
        public long? QualificacaoFieldId { get; set; }
        /// <summary>Campo "Tipo" da Kommo (resgate/ligação/mensagem) — alimenta o breakdown do card Resgate.</summary>
        public long? TipoFieldId { get; set; }
        /// <summary>Campo "Tipo de agendamento" (consulta/retorno/avaliação) — breakdown do card Agendados.</summary>
        public long? TipoAgendamentoFieldId { get; set; }
        /// <summary>Campo "Tipo de tratamento" (fisioterapia/pilates/...) — breakdown do card Tratamentos.</summary>
        public long? TipoTratamentoFieldId { get; set; }
    }

    // ─── CRUD ────────────────────────────────────────────────────────────────

    public Task<List<KpiConfiguration>> GetForUnitAsync(int unitId, CancellationToken ct = default) =>
        _db.KpiConfigurations.AsNoTracking()
            .Where(k => k.UnitId == unitId)
            .OrderBy(k => k.KpiKey)
            .ToListAsync(ct);

    /// <summary>Upsert (por KpiKey) de cada mapeamento enviado. Não remove os ausentes.</summary>
    public async Task SaveAsync(
        int unitId, int clinicId,
        IEnumerable<KpiSaveItem> items,
        string? email, CancellationToken ct = default)
    {
        var existing = await _db.KpiConfigurations
            .Where(k => k.UnitId == unitId)
            .ToListAsync(ct);

        var now = DateTime.UtcNow;
        foreach (var item in items)
        {
            var row = existing.FirstOrDefault(e => e.KpiKey == item.KpiKey);
            if (row is null)
            {
                _db.KpiConfigurations.Add(new KpiConfiguration
                {
                    UnitId = unitId,
                    ClinicId = clinicId,
                    KpiKey = item.KpiKey,
                    SourceType = item.SourceType,
                    ConfigJson = item.ConfigJson,
                    IsCustom = item.IsCustom,
                    DisplayName = item.DisplayName,
                    AccentColor = item.AccentColor,
                    DisplayType = item.DisplayType,
                    SortOrder = item.SortOrder,
                    UpdatedByEmail = email,
                    CreatedAt = now,
                    UpdatedAt = now,
                });
            }
            else
            {
                row.SourceType = item.SourceType;
                row.ConfigJson = item.ConfigJson;
                row.IsCustom = item.IsCustom;
                row.DisplayName = item.DisplayName;
                row.AccentColor = item.AccentColor;
                row.DisplayType = item.DisplayType;
                row.SortOrder = item.SortOrder;
                row.UpdatedByEmail = email;
                row.UpdatedAt = now;
            }
        }

        await _db.SaveChangesAsync(ct);
    }

    /// <summary>Remove um KPI (usado para apagar KPIs custom). No-op se não existir.</summary>
    public async Task<bool> DeleteAsync(int unitId, string kpiKey, CancellationToken ct = default)
    {
        var row = await _db.KpiConfigurations
            .FirstOrDefaultAsync(k => k.UnitId == unitId && k.KpiKey == kpiKey, ct);
        if (row is null) return false;
        _db.KpiConfigurations.Remove(row);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    // ─── Motor de cálculo ────────────────────────────────────────────────────

    /// <summary>
    /// Calcula o valor de um KPI dado o tipo de fonte e os parâmetros. Retorna o número
    /// e o tamanho da amostra (total de leads do período no escopo unidade/tenant).
    /// </summary>
    public async Task<(double Value, int Sample, string? Note)> ComputeAsync(
        int clinicId, int? unitId, string sourceType, JsonElement config,
        DateTime from, DateTime to, string? responsibleUser = null, string? kpiKey = null,
        CancellationToken ct = default)
    {
        from = AsUtc(from); to = AsUtc(to);
        // Janela pela DATA REAL de criação do lead (mesma regra do DashboardOverview):
        // OriginalCreatedAt (custom field "Data de criação lead", vindo da Kommo ou do
        // backfill da Cloudia) com fallback pro CreatedAt do backend. Sem isso, leads
        // migrados da Cloudia ficam todos amontoados no mês da migração e desalinham
        // do card "Total de Leads".
        var baseQuery = _db.Leads.AsNoTracking()
            .ExcludeDeleted()
            .Where(l => l.TenantId == clinicId
                     && (l.OriginalCreatedAt ?? l.CreatedAt) >= from
                     && (l.OriginalCreatedAt ?? l.CreatedAt) <= to);
        if (unitId.HasValue)
            baseQuery = baseQuery.Where(l => l.UnitId == unitId.Value);
        baseQuery = await ResponsibleUserFilter.ApplyAsync(baseQuery, responsibleUser, ct);

        // Leads marcados como "não contar" pelo admin (kpi_exclusions) saem de QUALQUER
        // fonte deste KPI — antes só o caminho do funil/breakdown filtrava, e o override
        // (kpi_overrides) ignorava a exclusão, então o card não diminuía. kpiKey vem null
        // no preview (ainda não salvo), aí não há o que excluir.
        var excluded = new List<int>();
        if (!string.IsNullOrWhiteSpace(kpiKey))
        {
            excluded = await _db.KpiExclusions.AsNoTracking()
                .Where(e => e.TenantId == clinicId && e.KpiKey == kpiKey
                         && (!unitId.HasValue || e.UnitId == unitId.Value))
                .Select(e => e.LeadId)
                .ToListAsync(ct);
            if (excluded.Count > 0)
                baseQuery = baseQuery.Where(l => !excluded.Contains(l.Id));
        }

        var sample = await baseQuery.CountAsync(ct);
        var p = ParseConfig(config);

        switch (sourceType)
        {
            case KpiSourceTypes.CreatedInPeriod:
                // Todos os leads criados no período (ex.: "Total de Leads").
                return (sample, sample, null);

            case KpiSourceTypes.KommoStage:
            {
                if (p.StageIds.Count == 0)
                    return (0, sample, "Selecione ao menos uma etapa.");
                var ids = p.StageIds;

                // Conta por ENTRADA na etapa (LeadStageHistory dentro do período) —
                // não por CurrentStageId + CreatedAt. Sem isso, lead criado em maio
                // que entrou em "Agendado" em 09/06 some do KPI de 09/06 (era o bug
                // que o KpiBreakdownsAsync já corrigiu em 6f29169, mas que faltava
                // espelhar aqui no source KommoStage usado pelos overrides).
                var scope = _db.Leads.AsNoTracking().ExcludeDeleted().Where(l => l.TenantId == clinicId);
                if (unitId.HasValue) scope = scope.Where(l => l.UnitId == unitId.Value);
                scope = await ResponsibleUserFilter.ApplyAsync(scope, responsibleUser, ct);
                if (excluded.Count > 0) scope = scope.Where(l => !excluded.Contains(l.Id));

                // Janela usa a data CORRIGIDA quando o admin ajustou a transição
                // (ex.: SDR moveu o lead no dia errado), senão a original.
                var entryLeadIds = await _db.LeadStageHistories.AsNoTracking()
                    .Where(h => ids.Contains(h.StageId)
                        && h.EntrySource != LeadStageHistory.SourceLegacy
                        && (h.CorrectedChangedAt ?? h.ChangedAt) >= from
                        && (h.CorrectedChangedAt ?? h.ChangedAt) <= to)
                    .Join(scope, h => h.LeadId, l => l.Id, (h, l) => h.LeadId)
                    .ToListAsync(ct);
                var entryCountByLead = entryLeadIds
                    .GroupBy(id => id).ToDictionary(g => g.Key, g => g.Count());

                // Agendados: desconta RECLASSIFICAÇÕES (lead que já era agendado antes do
                // período e só bounceou 04↔05 dentro) — mesma regra do card/breakdown e do
                // funil, pra que o número grande bata com os chips. Outros KPIs KommoStage
                // seguem contando toda entrada no período (comportamento inalterado).
                if (kpiKey == "agendados")
                {
                    var reclass = await LoadReclassifiedLeadIdsAsync(ids, entryCountByLead, ct);
                    return (entryCountByLead.Keys.Count(id => !reclass.Contains(id)), sample, null);
                }
                return (entryCountByLead.Count, sample, null);
            }

            case KpiSourceTypes.CustomFieldCount:
            case KpiSourceTypes.CustomFieldSum:
            case KpiSourceTypes.StageFieldFilter:
            {
                if (p.FieldId is null && string.IsNullOrWhiteSpace(p.FieldCode))
                    return (0, sample, "Selecione o campo customizado.");

                var q = baseQuery;
                if (sourceType == KpiSourceTypes.StageFieldFilter && p.StageIds.Count > 0)
                {
                    var ids = p.StageIds;
                    q = q.Where(l => l.CurrentStageId != null && ids.Contains(l.CurrentStageId.Value));
                }

                var rows = await q
                    .Where(l => l.CustomFieldsJson != null)
                    .Select(l => l.CustomFieldsJson!)
                    .ToListAsync(ct);

                if (sourceType == KpiSourceTypes.CustomFieldSum)
                {
                    double sum = 0;
                    foreach (var json in rows)
                    {
                        var v = ExtractFieldValue(json, p.FieldId, p.FieldCode);
                        if (v != null && TryParseNumber(v, out var num)) sum += num;
                    }
                    return (sum, sample, null);
                }

                int matched = 0;
                foreach (var json in rows)
                {
                    var v = ExtractFieldValue(json, p.FieldId, p.FieldCode);
                    if (v is null) continue;
                    if (p.MatchValues.Count == 0) { matched++; continue; } // "campo preenchido"
                    if (p.MatchValues.Any(m => string.Equals(m.Trim(), v.Trim(), StringComparison.OrdinalIgnoreCase)))
                        matched++;
                }
                return (matched, sample, null);
            }

            case KpiSourceTypes.RecoveryAttempt:
            {
                // Conta leads DISTINTOS com tentativa de resgate dentro do período pela
                // data do EVENTO na Kommo. Duas fontes confiáveis:
                //  • "events_api" — backfill noturno via API de eventos da Kommo
                //    (ResgateAttemptBackfillService); CreatedAt = data exata do evento.
                //  • "webhook" — gravação ao vivo via KommoIngestionService quando o
                //    "Tentativas de resgastes" é preenchido; CreatedAt = updated_at do
                //    lead na Kommo (close enough — KPI agrega por dia).
                // Excluímos "manual" porque CreatedAt = DateTime.UtcNow do backend, não
                // confiável p/ agregar por dia ("preenchi semana passada" cairia hoje).
                var scope = _db.Leads.AsNoTracking().ExcludeDeleted().Where(l => l.TenantId == clinicId);
                if (unitId.HasValue) scope = scope.Where(l => l.UnitId == unitId.Value);
                scope = await ResponsibleUserFilter.ApplyAsync(scope, responsibleUser, ct);
                if (excluded.Count > 0) scope = scope.Where(l => !excluded.Contains(l.Id));

                var count = await _db.RecoveryAttempts.AsNoTracking()
                    .Where(r => (r.EntrySource == "events_api" || r.EntrySource == "webhook")
                        && r.CreatedAt >= from && r.CreatedAt <= to)
                    .Join(scope, r => r.LeadId, l => l.Id, (r, l) => r.LeadId)
                    .Distinct()
                    .CountAsync(ct);
                return (count, sample, null);
            }

            default:
                return (0, sample, $"Tipo de fonte desconhecido: {sourceType}");
        }
    }

    /// <summary>
    /// Drill-down: devolve os leads por trás de um KPI (mesma lógica de filtro do
    /// ComputeAsync, mas retornando os leads em vez do número). Cap em <paramref name="limit"/>.
    /// </summary>
    public async Task<(List<DTOs.Kpi.KpiLeadDto> Items, int Total, bool Truncated)> ComputeLeadsAsync(
        int clinicId, int? unitId, string sourceType, JsonElement config,
        DateTime from, DateTime to, int limit = 500, CancellationToken ct = default,
        string? kpiKey = null)
    {
        const int MaxScan = 5000; // teto de varredura p/ filtro em memória
        from = AsUtc(from); to = AsUtc(to);

        // Lista de leads marcados como "não contar" pelo admin pra esse KPI/unidade.
        // No drill-down NÃO filtramos eles (admin precisa ver pra desmarcar) — só
        // setamos Excluded=true no DTO pra UI mostrar diferente.
        HashSet<int> excludedSet = new();
        if (!string.IsNullOrWhiteSpace(kpiKey))
        {
            var excludedIds = await _db.KpiExclusions.AsNoTracking()
                .Where(e => e.TenantId == clinicId && e.KpiKey == kpiKey
                         && (!unitId.HasValue || e.UnitId == unitId.Value))
                .Select(e => e.LeadId)
                .ToListAsync(ct);
            excludedSet = excludedIds.ToHashSet();
        }

        var p = ParseConfig(config);
        var fieldBased = sourceType is KpiSourceTypes.CustomFieldCount
            or KpiSourceTypes.CustomFieldSum or KpiSourceTypes.StageFieldFilter;

        // Cadastro/Resgate: o drill filtra pelo TIPO do lead (campo "Tipo" mapeado),
        // espelhando o número do card (KpiBreakdownsAsync). Carrega o mapeamento só quando
        // precisa, pra não pagar a query nos outros KPIs. EXCEÇÃO: quando o Resgate foi mapeado
        // pra "Campo (contagem por valor)" (fieldBased), o próprio filtro do campo já espelha o
        // card — aplicar Tipo=resgate por cima derrubaria a lista pro número antigo.
        var tipoFilter = kpiKey == "cadastro" || (kpiKey == "resgate" && !fieldBased);
        long? tipoFieldId = null;
        if (tipoFilter && unitId.HasValue)
            tipoFieldId = (await GetLeadProfileConfigAsync(unitId.Value, ct)).TipoFieldId;

        // Quando a fonte vem do stage_history (KommoStage), guardamos pra cada lead a
        // transição MAIS RECENTE no período — o front usa o id pra abrir o editor de
        // "corrigir data" direto do drill-down (sem ir pra página de auditoria).
        Dictionary<int, (int HistoryId, DateTime EffectiveAt)>? historyByLead = null;

        IQueryable<LeadAnalytics.Api.Models.Lead> q;
        if (sourceType == KpiSourceTypes.KommoStage)
        {
            // Drill: leads que ENTRARAM na etapa no período (espelha ComputeAsync).
            if (p.StageIds.Count == 0) return (new(), 0, false);
            var ids = p.StageIds;

            var scope = _db.Leads.AsNoTracking().ExcludeDeleted().Where(l => l.TenantId == clinicId);
            if (unitId.HasValue) scope = scope.Where(l => l.UnitId == unitId.Value);

            var historyRows = await _db.LeadStageHistories.AsNoTracking()
                .Where(h => ids.Contains(h.StageId)
                    && h.EntrySource != LeadStageHistory.SourceLegacy
                    && (h.CorrectedChangedAt ?? h.ChangedAt) >= from
                    && (h.CorrectedChangedAt ?? h.ChangedAt) <= to)
                .Join(scope, h => h.LeadId, l => l.Id,
                    (h, l) => new { h.Id, h.LeadId, EffectiveAt = (h.CorrectedChangedAt ?? h.ChangedAt) })
                .ToListAsync(ct);

            // Lead pode reentrar na etapa dentro do período — pegamos a entrada mais recente
            // pra ser o "alvo" do botão "corrigir data". Lead errado num mesmo dia é raro;
            // se a SDR quiser editar uma entrada anterior, a página de auditoria mostra todas.
            historyByLead = historyRows
                .GroupBy(r => r.LeadId)
                .ToDictionary(g => g.Key,
                    g => g.OrderByDescending(r => r.EffectiveAt).Select(r => (r.Id, r.EffectiveAt)).First());

            // Agendados: tira as RECLASSIFICAÇÕES da lista (leads que já eram agendados antes
            // do período) — assim a contagem do drill bate com o número grande e os chips.
            if (kpiKey == "agendados")
            {
                var inPeriodCount = historyRows
                    .GroupBy(r => r.LeadId).ToDictionary(g => g.Key, g => g.Count());
                var reclass = await LoadReclassifiedLeadIdsAsync(ids, inPeriodCount, ct);
                if (reclass.Count > 0)
                    foreach (var id in reclass) historyByLead.Remove(id);
            }

            var leadIds = historyByLead.Keys.ToList();
            q = _db.Leads.AsNoTracking().ExcludeDeleted().Where(l => leadIds.Contains(l.Id));
        }
        else if (sourceType == KpiSourceTypes.RecoveryAttempt)
        {
            // Drill: leads com tentativa de resgate no período (espelha ComputeAsync).
            var scope = _db.Leads.AsNoTracking().ExcludeDeleted().Where(l => l.TenantId == clinicId);
            if (unitId.HasValue) scope = scope.Where(l => l.UnitId == unitId.Value);

            var leadIds = await _db.RecoveryAttempts.AsNoTracking()
                .Where(r => (r.EntrySource == "events_api" || r.EntrySource == "webhook")
                    && r.CreatedAt >= from && r.CreatedAt <= to)
                .Join(scope, r => r.LeadId, l => l.Id, (r, l) => r.LeadId)
                .Distinct()
                .ToListAsync(ct);

            q = _db.Leads.AsNoTracking().ExcludeDeleted().Where(l => leadIds.Contains(l.Id));
        }
        else
        {
            // Demais sources janelam pela DATA REAL de criação do lead (espelha
            // ComputeAsync e DashboardOverview): OriginalCreatedAt ?? CreatedAt.
            q = _db.Leads.AsNoTracking()
                .ExcludeDeleted()
                .Where(l => l.TenantId == clinicId
                         && (l.OriginalCreatedAt ?? l.CreatedAt) >= from
                         && (l.OriginalCreatedAt ?? l.CreatedAt) <= to);
            if (unitId.HasValue)
                q = q.Where(l => l.UnitId == unitId.Value);

            if (sourceType == KpiSourceTypes.StageFieldFilter && p.StageIds.Count > 0)
            {
                var ids = p.StageIds;
                q = q.Where(l => l.CurrentStageId != null && ids.Contains(l.CurrentStageId.Value));
            }
        }

        q = q.OrderByDescending(l => l.CreatedAt);

        var hits = new List<DTOs.Kpi.KpiLeadDto>();
        var scanned = 0;
        var truncated = false;

        var rows = await q.Take(MaxScan).Select(l => new
        {
            l.Id, l.ExternalId, l.Name, l.Phone, l.Source, l.Channel,
            l.CurrentStage, l.CurrentStageId, l.LeadType, l.HasAppointment, l.HasPayment,
            l.CreatedAt, l.CustomFieldsJson,
            l.AppointmentScheduledAt, l.ConsultationValue, l.ClosedTreatment,
        }).ToListAsync(ct);

        truncated = rows.Count >= MaxScan;

        foreach (var l in rows)
        {
            scanned++;
            string? matched = null;

            if (fieldBased)
            {
                matched = ExtractFieldValue(l.CustomFieldsJson ?? "[]", p.FieldId, p.FieldCode);
                if (matched is null) continue; // campo não preenchido
                if (sourceType != KpiSourceTypes.CustomFieldSum && p.MatchValues.Count > 0 &&
                    !p.MatchValues.Any(m => string.Equals(m.Trim(), matched.Trim(), StringComparison.OrdinalIgnoreCase)))
                    continue;
            }

            // Cadastro/Resgate: mantém só os leads do tipo certo (campo "Tipo" mapeado) —
            // assim a lista e o resumo "por origem" batem com o número do card.
            if (tipoFilter)
            {
                var tipoResolved = ResolveTipoField(l.CustomFieldsJson, l.LeadType, tipoFieldId);
                if (kpiKey == "cadastro" && !IsCadastroTipo(tipoResolved)) continue;
                if (kpiKey == "resgate" && !IsResgateTipo(tipoResolved)) continue;
            }

            var cf = l.CustomFieldsJson;
            // Pega a entrada mais recente no stage_history pra essa fonte (só p/ KommoStage)
            // — é o id que o front passa pro PATCH /corrected-date.
            var hist = historyByLead != null && historyByLead.TryGetValue(l.Id, out var h)
                ? ((int?)h.HistoryId, (DateTime?)h.EffectiveAt)
                : (null, null);
            hits.Add(new DTOs.Kpi.KpiLeadDto
            {
                Id = l.Id,
                ExternalId = l.ExternalId,
                Name = l.Name,
                Phone = l.Phone,
                Source = l.Source,
                Channel = l.Channel,
                CurrentStage = l.CurrentStage,
                CurrentStageId = l.CurrentStageId,
                LeadType = l.LeadType,
                HasAppointment = l.HasAppointment,
                HasPayment = l.HasPayment,
                CreatedAt = l.CreatedAt,
                MatchedValue = matched,
                AppointmentAt = l.AppointmentScheduledAt,
                ConsultationValue = l.ConsultationValue,
                ClosedTreatment = l.ClosedTreatment,
                MotivoNaoAgendamento = ExtractFieldByName(cf, n => n.Contains("motivo") && n.Contains("agendamento")),
                TratamentoFechado = ExtractFieldByName(cf, n => n.Contains("tratamento") && n.Contains("fechad")),
                ResponsavelAgendamento = ExtractFieldByName(cf, n =>
                    (n.Contains("responsável") || n.Contains("responsavel")) && n.Contains("agendamento")
                    || n.Contains("fisio") || n.Contains("doutor")),
                Qualificacao = ExtractFieldByName(cf, n => n.Contains("qualifica")),
                OrigemCustom = ExtractFieldByName(cf, n => n.Contains("origem")),
                TreatmentValue = TryParseDecimal(ExtractFieldByName(cf, n => n.Contains("valor") && n.Contains("tratamento"))),
                Excluded = excludedSet.Contains(l.Id),
                HistoryId = hist.Item1,
                EffectiveChangedAt = hist.Item2,
                CustomFields = ExtractFilledFields(cf),
            });

            if (hits.Count >= limit) { truncated = true; break; }
        }

        return (hits, hits.Count, truncated);
    }

    /// <summary>
    /// Breakdowns por KPI do dashboard principal: cadastro/resgate/agendados/tratamentos/consultas.
    /// Uma única varredura de leads do período + extração de campos customizados (origem, motivo,
    /// fisio, valor tratamento). Renderiza inline em cada KPI card.
    /// </summary>
    public async Task<DTOs.Dashboard.KpiBreakdownsDto> KpiBreakdownsAsync(
        int clinicId, int? unitId, DateTime from, DateTime to, CancellationToken ct = default)
    {
        const int MaxScan = 8000;
        from = AsUtc(from); to = AsUtc(to);

        // Janela pela DATA REAL: OriginalCreatedAt quando setada (Kommo/CSV), CreatedAt como fallback.
        // ExcludeDeleted: leads marcados como "deleted" pelo webhook não contam.
        var q = _db.Leads.AsNoTracking()
            .ExcludeDeleted()
            .Where(l => l.TenantId == clinicId
                     && (l.OriginalCreatedAt ?? l.CreatedAt) >= from
                     && (l.OriginalCreatedAt ?? l.CreatedAt) <= to);
        if (unitId.HasValue)
            q = q.Where(l => l.UnitId == unitId.Value);

        // Mapeamento de campos da Kommo escolhido pelo analista (Configurações → Perfil do Lead).
        var profile = unitId.HasValue
            ? await GetLeadProfileConfigAsync(unitId.Value, ct)
            : new LeadProfileFields();

        // Stages mapeados pra "tratamentos" em Configurações Técnicas (kommo_stage).
        // Se setado, o breakdown deste card calcula por ENTRADA nessas etapas no período
        // (mesma semântica do kpi_overrides), em vez do FechouTratamento hardcoded — sem
        // isso, a unidade que usa "Em Tratamento" como estágio final vê count=19 no card
        // mas Origem/Fisio/Valor vazios porque o loop abaixo nunca casa o lead.
        List<int>? tratStageIds = null;
        if (unitId.HasValue)
        {
            var tratCfg = await _db.KpiConfigurations.AsNoTracking()
                .FirstOrDefaultAsync(k => k.UnitId == unitId.Value && k.KpiKey == "tratamentos", ct);
            if (tratCfg is { SourceType: KpiSourceTypes.KommoStage }
                && !string.IsNullOrWhiteSpace(tratCfg.ConfigJson))
            {
                try
                {
                    var parsed = ParseConfig(JsonSerializer.Deserialize<JsonElement>(tratCfg.ConfigJson));
                    if (parsed.StageIds.Count > 0) tratStageIds = parsed.StageIds;
                }
                catch (JsonException) { /* config inválida — cai pro hardcoded */ }
            }
        }

        // Stages mapeados pra "agendados" em Configurações Técnicas (kommo_stage). Quando a
        // unidade mapeia esse KPI por ETAPA (é o que alimenta o número grande via kpi_overrides
        // e o drill-down, ambos por StageId), o breakdown TEM que casar pelas mesmas etapas —
        // senão o card mostra "1" mas os chips (Pagamento/Origem/Tipo) ficam "sem dados" porque
        // o StageLabel gravado no histórico não é exatamente a constante hardcoded LeadStages.*.
        List<int>? agStageIds = null;
        if (unitId.HasValue)
        {
            var agCfg = await _db.KpiConfigurations.AsNoTracking()
                .FirstOrDefaultAsync(k => k.UnitId == unitId.Value && k.KpiKey == "agendados", ct);
            if (agCfg is { SourceType: KpiSourceTypes.KommoStage }
                && !string.IsNullOrWhiteSpace(agCfg.ConfigJson))
            {
                try
                {
                    var parsed = ParseConfig(JsonSerializer.Deserialize<JsonElement>(agCfg.ConfigJson));
                    if (parsed.StageIds.Count > 0) agStageIds = parsed.StageIds;
                }
                catch (JsonException) { /* config inválida — cai pro hardcoded */ }
            }
        }

        // Resgate mapeado como "Campo (contagem por valor)" em Configurações Técnicas: em vez
        // de classificar por Tipo=resgate, o card quebra pelos VALORES do campo (ex.: o campo
        // "Tentativas de resgastes" → Resgate 1-24h, 2-48h, …). resgateField != null liga esse
        // modo; senão mantém o comportamento hardcoded (campo "Tipo").
        ParsedConfig? resgateField = null;
        if (unitId.HasValue)
        {
            var resCfg = await _db.KpiConfigurations.AsNoTracking()
                .FirstOrDefaultAsync(k => k.UnitId == unitId.Value && k.KpiKey == "resgate", ct);
            if (resCfg is { SourceType: KpiSourceTypes.CustomFieldCount }
                && !string.IsNullOrWhiteSpace(resCfg.ConfigJson))
            {
                try
                {
                    var parsed = ParseConfig(JsonSerializer.Deserialize<JsonElement>(resCfg.ConfigJson));
                    if (parsed.FieldId is not null || !string.IsNullOrWhiteSpace(parsed.FieldCode))
                        resgateField = parsed;
                }
                catch (JsonException) { /* config inválida — cai pro Tipo hardcoded */ }
            }
        }

        var rows = await q.OrderByDescending(l => l.CreatedAt).Take(MaxScan)
            .Select(l => new
            {
                l.Name, l.Source, l.LeadType, l.CurrentStage, l.HasPayment,
                l.AppointmentScheduledAt, l.ConsultationValue, l.CustomFieldsJson,
            }).ToListAsync(ct);

        // Aggregators
        var cad = new DTOs.Dashboard.CadastroBreakdownDto();
        var cadByOrigem = new Dictionary<string, (int Count, Dictionary<string, int> Motivos)>(StringComparer.OrdinalIgnoreCase);

        var resgateTipos = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var resgateOrigens = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var resgateTotal = 0;

        var ag = new DTOs.Dashboard.AgendadosBreakdownDto();
        var agOrigens = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var agTipos = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        var trat = new DTOs.Dashboard.TratamentosBreakdownDto();
        var tratOrigens = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var tratFisios = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var tratTipos = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        var cons = new DTOs.Dashboard.ConsultasBreakdownDto();
        var consUpcoming = new List<DTOs.Dashboard.AgendamentoItemDto>();

        static bool IsResgate(string? t) => !string.IsNullOrEmpty(t) && t.Contains("resgate", StringComparison.OrdinalIgnoreCase);
        static bool IsCadastro(string? t) => string.IsNullOrEmpty(t) || t.Contains("cadastro", StringComparison.OrdinalIgnoreCase) || t.Contains("novo", StringComparison.OrdinalIgnoreCase);

        // Prefere a coluna LeadType — é a MESMA fonte do número grande do card Cadastro
        // (LeadService.cadastroTotal), então breakdown/drill batem com ele (197 = 197).
        // Cai pro custom field "Tipo" mapeado só quando LeadType vem vazio (unidades onde
        // a coluna não é preenchida). Antes preferia o custom field, o que inflava o
        // breakdown (197 vs 330) quando os dois discordavam.
        string? ResolveTipo(string? cf, string? leadType)
        {
            if (!string.IsNullOrWhiteSpace(leadType)) return leadType.Trim();
            return ExtractField(cf, profile.TipoFieldId, n => n == "tipo")?.Trim();
        }

        foreach (var l in rows)
        {
            var cf = l.CustomFieldsJson;
            // Prefere o fieldId mapeado pelo analista (Configurações → Perfil do Lead).
            // Se não houver, cai pra match por nome.
            var origemCustom = ExtractField(cf, profile.OrigemFieldId, n => n.Contains("origem"));
            var origem = !string.IsNullOrWhiteSpace(origemCustom) ? origemCustom.Trim()
                       : !string.IsNullOrWhiteSpace(l.Source) ? l.Source.Trim()
                       : "—";

            var motivo = ExtractField(cf, profile.MotivoNaoAgendamentoFieldId,
                n => n.Contains("motivo") && n.Contains("agendamento"))?.Trim();
            var fisio = ExtractField(cf, profile.FisioterapeutaFieldId ?? profile.DoctorFieldId,
                n => ((n.Contains("responsável") || n.Contains("responsavel")) && n.Contains("agendamento"))
                  || n.Contains("fisio") || n.Contains("doutor"))?.Trim();
            var valorTratStr = ExtractField(cf, profile.ValorTratamentoFieldId,
                n => n.Contains("valor") && n.Contains("tratamento"));
            var valorTrat = TryParseDecimal(valorTratStr) ?? 0m;

            // Resolve tipo prefere o custom field "Tipo" mapeado (ResolveTipo),
            // que é onde as moças marcam Cadastro/Resgate — LeadType (SQL) raramente
            // está preenchido.
            var tipoResolved = ResolveTipo(cf, l.LeadType);
            // Cadastro × Resgate: classificados SÓ pelo tipo resolvido (campo "Tipo" mapeado,
            // fallback LeadType). Mutuamente exclusivos — IsCadastro pega vazio/"cadastro"/"novo",
            // IsResgate exige "resgate". A contagem de Resgate é feita logo abaixo, no mesmo loop
            // (não usa mais recovery_attempts). hasResgate/tentativasResgate ficam só como sinal
            // auxiliar (não entram na classificação por decisão de produto: fonte = campo Tipo).
            var leadIsCadastro = IsCadastro(tipoResolved);

            var stage = l.CurrentStage ?? "";
            // Agendados NÃO são contados aqui — são contados por data de ENTRADA na
            // etapa (histórico), num passo separado abaixo, pra incluir leads antigos
            // agendados dentro do período.
            var isConsulta = stage == LeadStages.EmTratamento || stage == LeadStages.FechouTratamento || stage == LeadStages.NaoFechouTratamento;
            // Quando a unidade mapeou "tratamentos" pra outras etapas (kpi_overrides),
            // pulamos o cálculo inline aqui e populamos lá embaixo via LeadStageHistory.
            var isTratamento = tratStageIds is null && stage == LeadStages.FechouTratamento;

            // ── Cadastro
            if (leadIsCadastro)
            {
                cad.Total++;
                if (!cadByOrigem.TryGetValue(origem, out var row))
                {
                    row = (0, new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase));
                    cadByOrigem[origem] = row;
                }
                row.Count++;
                if (!string.IsNullOrWhiteSpace(motivo))
                    row.Motivos[motivo] = row.Motivos.GetValueOrDefault(motivo) + 1;
                cadByOrigem[origem] = row;
            }

            // ── Resgate: leads cujo TIPO (campo "Tipo" mapeado em Configurações → Perfil do
            // Lead) = resgate. Conta na criação do lead no período, IGUAL ao Cadastro —
            // classificado SÓ pelo campo Tipo (não usa mais recovery_attempts). Cadastro e
            // Resgate ficam mutuamente exclusivos: IsResgate exige "resgate", IsCadastro pega
            // vazio/"cadastro"/"novo".
            if (resgateField is not null)
            {
                // Fonte = campo customizado: conta o lead se o campo está preenchido (e, quando
                // há matchValues, se casa) e quebra a distribuição pelos valores do campo —
                // espelha ComputeAsync(custom_field_count), então número/breakdown/drill batem.
                var rv = ExtractFieldValue(cf ?? "[]", resgateField.FieldId, resgateField.FieldCode);
                if (rv is not null &&
                    (resgateField.MatchValues.Count == 0 ||
                     resgateField.MatchValues.Any(m => string.Equals(m.Trim(), rv.Trim(), StringComparison.OrdinalIgnoreCase))))
                {
                    resgateTotal++;
                    var key = rv.Trim();
                    resgateTipos[key] = resgateTipos.GetValueOrDefault(key) + 1;
                    resgateOrigens[origem] = resgateOrigens.GetValueOrDefault(origem) + 1;
                }
            }
            else if (IsResgate(tipoResolved))
            {
                resgateTotal++;
                var tipoLabel = !string.IsNullOrWhiteSpace(tipoResolved) ? tipoResolved!.Trim() : "Resgate";
                resgateTipos[tipoLabel] = resgateTipos.GetValueOrDefault(tipoLabel) + 1;
                resgateOrigens[origem] = resgateOrigens.GetValueOrDefault(origem) + 1;
            }

            // ── Tratamentos
            if (isTratamento)
            {
                trat.Total++;
                tratOrigens[origem] = tratOrigens.GetValueOrDefault(origem) + 1;
                if (!string.IsNullOrWhiteSpace(fisio))
                    tratFisios[fisio] = tratFisios.GetValueOrDefault(fisio) + 1;
                if (l.ConsultationValue.HasValue) trat.ValorConsultaTotal += l.ConsultationValue.Value;
                if (valorTrat > 0) trat.ValorTratamentoTotal += valorTrat;
                // Tipo de tratamento (custom field "Tipo de tratamento" mapeado em
                // Configurações; fallback por nome "tipo" + "tratamento").
                var tipoTrat = ExtractField(cf, profile.TipoTratamentoFieldId,
                    n => n.Contains("tipo") && n.Contains("tratamento"))?.Trim();
                if (!string.IsNullOrWhiteSpace(tipoTrat))
                    tratTipos[tipoTrat] = tratTipos.GetValueOrDefault(tipoTrat) + 1;
            }

            // ── Consultas NÃO contam aqui — vão pela "Data de agendamento" no range,
            //    independente da etapa atual. Query separada logo após o loop.
        }

        // ── Tratamentos via stages mapeados (kpi_overrides): conta lead que ENTROU em
        //    uma das etapas configuradas dentro do período, mesma semântica do
        //    ComputeAsync(KommoStage). Pega Source/CustomFieldsJson/ConsultationValue
        //    do próprio Lead pra montar os mesmos chips do caminho hardcoded.
        if (tratStageIds is { Count: > 0 })
        {
            // Leads marcados como "não contar" pelo admin neste KPI ficam de fora.
            var tratExcluded = await _db.KpiExclusions.AsNoTracking()
                .Where(e => e.TenantId == clinicId && e.KpiKey == "tratamentos"
                    && (!unitId.HasValue || e.UnitId == unitId.Value))
                .Select(e => e.LeadId)
                .ToListAsync(ct);
            var tratHist = await _db.LeadStageHistories.AsNoTracking()
                .Where(h => tratStageIds.Contains(h.StageId)
                    && h.EntrySource != LeadStageHistory.SourceLegacy
                    && (h.CorrectedChangedAt ?? h.ChangedAt) >= from
                    && (h.CorrectedChangedAt ?? h.ChangedAt) <= to
                    && h.Lead!.TenantId == clinicId
                    && (!unitId.HasValue || h.Lead.UnitId == unitId.Value)
                    && !tratExcluded.Contains(h.LeadId))
                .Select(h => new {
                    h.LeadId, h.Lead!.Source, h.Lead.CustomFieldsJson, h.Lead.ConsultationValue,
                })
                .ToListAsync(ct);

            // Um lead pode reentrar nas etapas mapeadas no período — conta uma vez.
            foreach (var grp in tratHist.GroupBy(x => x.LeadId))
            {
                var l = grp.First();
                var cf = l.CustomFieldsJson;
                var origemCustom = ExtractField(cf, profile.OrigemFieldId, n => n.Contains("origem"));
                var origem = !string.IsNullOrWhiteSpace(origemCustom) ? origemCustom.Trim()
                           : !string.IsNullOrWhiteSpace(l.Source) ? l.Source!.Trim()
                           : "—";
                var fisio = ExtractField(cf, profile.FisioterapeutaFieldId ?? profile.DoctorFieldId,
                    n => ((n.Contains("responsável") || n.Contains("responsavel")) && n.Contains("agendamento"))
                      || n.Contains("fisio") || n.Contains("doutor"))?.Trim();
                var valorTratStr = ExtractField(cf, profile.ValorTratamentoFieldId,
                    n => n.Contains("valor") && n.Contains("tratamento"));
                var valorTrat = TryParseDecimal(valorTratStr) ?? 0m;
                var valorConsultaFromField = TryParseDecimal(ExtractField(cf, profile.ValorConsultaFieldId,
                    n => n.Contains("valor") && n.Contains("consulta")));

                trat.Total++;
                tratOrigens[origem] = tratOrigens.GetValueOrDefault(origem) + 1;
                if (!string.IsNullOrWhiteSpace(fisio))
                    tratFisios[fisio] = tratFisios.GetValueOrDefault(fisio) + 1;
                var consultaVal = l.ConsultationValue ?? valorConsultaFromField;
                if (consultaVal.HasValue) trat.ValorConsultaTotal += consultaVal.Value;
                if (valorTrat > 0) trat.ValorTratamentoTotal += valorTrat;
                var tipoTrat = ExtractField(cf, profile.TipoTratamentoFieldId,
                    n => n.Contains("tipo") && n.Contains("tratamento"))?.Trim();
                if (!string.IsNullOrWhiteSpace(tipoTrat))
                    tratTipos[tipoTrat] = tratTipos.GetValueOrDefault(tipoTrat) + 1;
            }
        }

        // ── Consultas: leads cujo CAMPO "Data de agendamento" foi PREENCHIDO dentro do
        //    range — mede produtividade da SDR no período (quantos agendamentos marcou),
        //    não a data da consulta em si. Pra "vai chegar hoje/amanhã" ver Próximos
        //    agendamentos abaixo (continua usando AppointmentScheduledAt).
        var consultasRows = await _db.Leads.AsNoTracking()
            .ExcludeDeleted()
            .Where(l => l.TenantId == clinicId
                && (!unitId.HasValue || l.UnitId == unitId.Value)
                && l.AppointmentScheduledAtFilledAt != null
                && l.AppointmentScheduledAtFilledAt >= from
                && l.AppointmentScheduledAtFilledAt <= to)
            .Select(l => new { l.LeadType, l.ConsultationValue, l.CustomFieldsJson })
            .ToListAsync(ct);
        foreach (var l in consultasRows)
        {
            cons.Total++;
            var tentativasResgate = ExtractField(l.CustomFieldsJson, null,
                n => n.Contains("tentativ") && n.Contains("resga"))?.Trim();
            var tipoResolved = ResolveTipo(l.CustomFieldsJson, l.LeadType);
            var isResgate = !string.IsNullOrWhiteSpace(tentativasResgate) || IsResgate(tipoResolved);
            if (isResgate) cons.Resgate++; else if (IsCadastro(tipoResolved)) cons.Cadastro++;
            var consVal = l.ConsultationValue ?? TryParseDecimal(ExtractField(l.CustomFieldsJson,
                profile.ValorConsultaFieldId,
                n => n.Contains("valor") && n.Contains("consulta")));
            if (consVal.HasValue) cons.ValorTotal += consVal.Value;
        }

        // ── Próximos agendamentos: leads com AppointmentScheduledAt FUTURO, independente
        //    de stage e do range comercial. Lê da coluna SQL (populada pelo sync via
        //    AppointmentFieldId) e cai pro CustomFieldsJson quando o sync ainda não
        //    rodou pra esse lead — assim funciona em dados novos e antigos.
        var apptNow = DateTime.UtcNow;
        var apptWindowEnd = apptNow.AddDays(60);
        var apptRows = await _db.Leads.AsNoTracking()
            .ExcludeDeleted()
            .Where(l => l.TenantId == clinicId
                && (!unitId.HasValue || l.UnitId == unitId.Value)
                && (l.AppointmentScheduledAt != null
                    || (l.CustomFieldsJson != null && profile.AppointmentFieldId != null)))
            .Select(l => new { l.Name, l.AppointmentScheduledAt, l.CustomFieldsJson, l.LeadType })
            .Take(MaxScan)
            .ToListAsync(ct);
        foreach (var l in apptRows)
        {
            DateTime? appt = l.AppointmentScheduledAt;
            if (appt is null)
            {
                var apptStr = ExtractField(l.CustomFieldsJson, profile.AppointmentFieldId, n => n.Contains("agendamento"));
                if (!string.IsNullOrWhiteSpace(apptStr) && TryParseDate(apptStr, out var d)) appt = d;
            }
            if (appt is null) continue;
            var apUtc = AsUtc(appt.Value);
            if (apUtc < apptNow || apUtc > apptWindowEnd) continue;
            consUpcoming.Add(new DTOs.Dashboard.AgendamentoItemDto
            {
                Name = l.Name,
                When = apUtc,
                Tipo = IsResgate(l.LeadType) ? "resgate" : "cadastro",
            });
        }

        // ── Consultas DO DIA: por DATA DA CONSULTA (AppointmentScheduledAt) dentro do
        //    range selecionado — é o número principal do card. Quebra por desfecho
        //    (compareceu/faltou/aguardando) lendo a etapa atual / status do lead.
        var consDia = await LoadConsultasDoDiaAsync(clinicId, unitId, profile, from, to, ct);
        cons.DoDia = consDia.Count;
        cons.Compareceu = consDia.Count(x => x.Outcome == "compareceu");
        cons.Faltou = consDia.Count(x => x.Outcome == "faltou");
        cons.Aguardando = consDia.Count(x => x.Outcome == "aguardando");

        // ── Agendados: contados por DATA DE ENTRADA na etapa (histórico de etapas),
        //    e não pela data de criação do lead — assim leads antigos agendados dentro
        //    do período aparecem. com/sem pagamento vem da própria etapa (04 vs 05).
        // Leads marcados como "não contar" pelo admin (kpi_exclusions) ficam de fora.
        var agStages = new[] { LeadStages.AgendadoSemPagamento, LeadStages.AgendadoComPagamento };
        var agExcluded = await _db.KpiExclusions.AsNoTracking()
            .Where(e => e.TenantId == clinicId && e.KpiKey == "agendados"
                && (!unitId.HasValue || e.UnitId == unitId.Value))
            .Select(e => e.LeadId)
            .ToListAsync(ct);
        var agExcludedSet = agExcluded.ToHashSet();
        // Fonte das etapas: se a unidade mapeou "agendados" por StageId (Configurações), casa
        // por esses ids — igual ao número grande (ComputeAsync) e ao drill-down. Senão, cai
        // pras etapas hardcoded 04/05 por StageLabel (comportamento legado das demais unidades).
        // O filtro de etapa é aplicado num .Where separado (evita ternário com dois .Contains
        // dentro do mesmo lambda, que o EF não traduz de forma confiável).
        var agHistQ = _db.LeadStageHistories.AsNoTracking()
            .Where(h => h.EntrySource != LeadStageHistory.SourceLegacy
                && (h.CorrectedChangedAt ?? h.ChangedAt) >= from
                && (h.CorrectedChangedAt ?? h.ChangedAt) <= to
                && h.Lead.TenantId == clinicId
                // ExcludeDeleted: leads deletados na Kommo (Status="deleted") não contam —
                // igual ao número grande (ComputeAsync) e ao drill. Sem isso o card divergia.
                && h.Lead.Status != LeadQueryExtensions.StatusDeleted
                && !agExcluded.Contains(h.LeadId)
                && (!unitId.HasValue || h.Lead.UnitId == unitId.Value));
        agHistQ = agStageIds != null
            ? agHistQ.Where(h => agStageIds.Contains(h.StageId))
            : agHistQ.Where(h => agStages.Contains(h.StageLabel));
        var agHist = await agHistQ
            .Select(h => new {
                h.LeadId, h.StageLabel, ChangedAt = (h.CorrectedChangedAt ?? h.ChangedAt),
                h.Lead.Source, h.Lead.LeadType, h.Lead.CustomFieldsJson,
                // Etapa ATUAL e HasPayment do lead — sem isso, lead que bounceou 04→05
                // (bounce intra-agendado NÃO gera nova linha de histórico) seguia contado
                // como "Sem pagamento" porque a linha do histórico ainda era 04.
                CurrentStage = h.Lead.CurrentStage, HasPayment = h.Lead.HasPayment,
            })
            .ToListAsync(ct);

        // Reclassificação: lead já tinha entrada REAL em agendado* ANTES do período. Conta
        // por NÚMERO DE LINHAS — se o lead tem mais linhas em agendado* no histórico total
        // do que as linhas que apareceram no período, já era agendado antes.
        // IMPORTANTE: só linhas non-legacy contam aqui, IGUAL ao agHist do período. Linhas
        // legacy são snapshots do sync (fotografam a etapa ATUAL, não uma entrada de verdade);
        // se entrassem no total, todo agendamento novo que também foi sincronizado parecia
        // "já era agendado antes" e virava reclassificação falsa (inflava muito o número).
        var agLeadIds = agHist.Select(x => x.LeadId).Distinct().ToList();
        var agCountQ = _db.LeadStageHistories.AsNoTracking()
            .Where(h => agLeadIds.Contains(h.LeadId)
                && h.EntrySource != LeadStageHistory.SourceLegacy);
        agCountQ = agStageIds != null
            ? agCountQ.Where(h => agStageIds.Contains(h.StageId))
            : agCountQ.Where(h => agStages.Contains(h.StageLabel));
        var agHistCountByLead = await agCountQ
            .GroupBy(h => h.LeadId)
            .Select(g => new { LeadId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.LeadId, x => x.Count, ct);

        // Um lead pode reentrar em agendado no período — conta uma vez (entrada mais recente).
        foreach (var grp in agHist.GroupBy(x => x.LeadId))
        {
            // Se o total de linhas em agendado do lead é MAIOR que as linhas no período,
            // existe entrada anterior — é reclassificação, não agendamento novo.
            var leadTotal = agHistCountByLead.GetValueOrDefault(grp.Key, 0);
            if (leadTotal > grp.Count())
            {
                ag.Reclassificacoes++;
                continue;
            }
            var h = grp.OrderByDescending(x => x.ChangedAt).First();
            var cf = h.CustomFieldsJson;
            var origemCustom = ExtractField(cf, profile.OrigemFieldId, n => n.Contains("origem"));
            var origem = !string.IsNullOrWhiteSpace(origemCustom) ? origemCustom.Trim()
                       : !string.IsNullOrWhiteSpace(h.Source) ? h.Source!.Trim()
                       : "—";
            var tentativasResgate = ExtractField(cf, null, n => n.Contains("tentativ") && n.Contains("resgat"))?.Trim();
            var tipoResolved = ResolveTipo(cf, h.LeadType);
            var leadIsResgate = !string.IsNullOrWhiteSpace(tentativasResgate) || IsResgate(tipoResolved);
            var leadIsCadastro = !leadIsResgate && IsCadastro(tipoResolved);

            ag.Total++;
            if (leadIsResgate) ag.Resgate++; else if (leadIsCadastro) ag.Cadastro++;
            // Pagamento antecipado: confia na etapa ATUAL ou no HasPayment do lead — não
            // no StageLabel histórico (bounce 04↔05 não cria nova linha). Cobre o lead
            // que entrou em 04 e foi promovido pra 05 (típico em ITZ).
            var isComPagamento = h.HasPayment || h.CurrentStage == LeadStages.AgendadoComPagamento;
            if (isComPagamento) ag.ComPagamento++; else ag.SemPagamento++;
            agOrigens[origem] = agOrigens.GetValueOrDefault(origem) + 1;
            // Tipo de agendamento (custom field "Tipo de agendamento" mapeado em
            // Configurações; fallback por nome "tipo" + "agendamento").
            var tipoAg = ExtractField(cf, profile.TipoAgendamentoFieldId,
                n => n.Contains("tipo") && n.Contains("agendamento"))?.Trim();
            if (!string.IsNullOrWhiteSpace(tipoAg))
                agTipos[tipoAg] = agTipos.GetValueOrDefault(tipoAg) + 1;
        }

        // ── Finaliza CadastroOrigens
        cad.Origens = cadByOrigem
            .Select(kv =>
            {
                var top = kv.Value.Motivos.OrderByDescending(m => m.Value).FirstOrDefault();
                return new DTOs.Dashboard.OrigemMotivoDto
                {
                    Origem = kv.Key,
                    Count = kv.Value.Count,
                    TopMotivo = string.IsNullOrEmpty(top.Key) ? null : top.Key,
                    TopMotivoCount = top.Value,
                };
            })
            .OrderByDescending(o => o.Count)
            .Take(8)
            .ToList();

        static List<DTOs.Dashboard.ValueCountDto> Top(Dictionary<string, int> d, int n = 6) =>
            d.OrderByDescending(kv => kv.Value).Take(n)
             .Select(kv => new DTOs.Dashboard.ValueCountDto { Value = kv.Key, Count = kv.Value })
             .ToList();

        // ── Resgate: agregado no loop principal por TIPO=resgate (campo "Tipo" mapeado),
        //    contando na criação do lead — mesma janela do Cadastro. resgateTotal/resgateTipos/
        //    resgateOrigens já foram populados acima. Tipos = valores distintos do campo Tipo
        //    (ex.: "Resgate", "Resgate - ligação"); Origens = origem do lead.
        var resgate = new DTOs.Dashboard.TipoOrigemBreakdownDto
        {
            Total = resgateTotal,
            Tipos = Top(resgateTipos, 8),
            Origens = Top(resgateOrigens),
        };
        ag.Origens = Top(agOrigens);
        ag.TiposAgendamento = Top(agTipos);
        trat.Origens = Top(tratOrigens);
        trat.Fisios = Top(tratFisios);
        trat.TiposTratamento = Top(tratTipos);
        cons.Agendamentos = consUpcoming.OrderBy(x => x.When).Take(8).ToList();

        return new DTOs.Dashboard.KpiBreakdownsDto
        {
            Cadastro = cad,
            Resgate = resgate,
            Agendados = ag,
            Tratamentos = trat,
            Consultas = cons,
        };
    }

    /// <summary>
    /// Métricas de TODOS os campos customizados dos leads do período: para cada campo,
    /// quantos leads o preenchem e a distribuição dos valores mais comuns. É o dado do
    /// dashboard "perfil do lead".
    /// </summary>
    public async Task<(int TotalLeads, List<DTOs.Kpi.CustomFieldSummaryDto> Fields, bool Truncated)>
        CustomFieldsSummaryAsync(int clinicId, int? unitId, DateTime from, DateTime to,
            int topValues = 8, CancellationToken ct = default)
    {
        const int MaxScan = 8000;
        from = AsUtc(from); to = AsUtc(to);

        // Filtra por UpdatedAt (não CreatedAt) — o webhook da Kommo atualiza
        // UpdatedAt + CustomFieldsJson juntos. Usar CreatedAt deixa de fora
        // leads antigos cujos campos foram preenchidos recentemente.
        var q = _db.Leads.AsNoTracking()
            .ExcludeDeleted()
            .Where(l => l.TenantId == clinicId && l.UpdatedAt >= from && l.UpdatedAt <= to
                        && l.CustomFieldsJson != null);
        if (unitId.HasValue)
            q = q.Where(l => l.UnitId == unitId.Value);

        var total = await q.CountAsync(ct);
        var jsons = await q.OrderByDescending(l => l.UpdatedAt)
            .Take(MaxScan).Select(l => l.CustomFieldsJson!).ToListAsync(ct);
        var truncated = jsons.Count >= MaxScan;

        // field_id -> agregador
        var agg = new Dictionary<long, FieldAgg>();
        foreach (var json in jsons)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind != JsonValueKind.Array) continue;
                foreach (var el in doc.RootElement.EnumerateArray())
                {
                    if (el.ValueKind != JsonValueKind.Object) continue;
                    if (!el.TryGetProperty("field_id", out var fidEl) || !TryGetLong(fidEl, out var fid)) continue;

                    var value = el.TryGetProperty("value", out var v)
                        ? (v.ValueKind == JsonValueKind.String ? v.GetString()
                           : v.ValueKind == JsonValueKind.Number ? v.GetRawText() : null)
                        : null;
                    if (string.IsNullOrWhiteSpace(value)) continue;

                    if (!agg.TryGetValue(fid, out var a))
                    {
                        a = new FieldAgg
                        {
                            Name = el.TryGetProperty("field_name", out var fn) && fn.ValueKind == JsonValueKind.String
                                ? fn.GetString() ?? $"Campo {fid}" : $"Campo {fid}",
                            Code = el.TryGetProperty("field_code", out var fc) && fc.ValueKind == JsonValueKind.String
                                ? fc.GetString() : null,
                            Type = el.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String
                                ? t.GetString() ?? "text" : "text",
                        };
                        agg[fid] = a;
                    }
                    a.Filled++;
                    var key = value.Trim();
                    a.Values[key] = a.Values.GetValueOrDefault(key, 0) + 1;
                }
            }
            catch (JsonException) { /* ignora json malformado */ }
        }

        var fields = agg
            .Select(kv => new DTOs.Kpi.CustomFieldSummaryDto
            {
                FieldId = kv.Key,
                FieldName = kv.Value.Name,
                FieldCode = kv.Value.Code,
                Type = kv.Value.Type,
                Filled = kv.Value.Filled,
                DistinctValues = kv.Value.Values.Count,
                TopValues = kv.Value.Values
                    .OrderByDescending(x => x.Value)
                    .Take(topValues)
                    .Select(x => new DTOs.Kpi.CustomFieldValueCountDto { Value = x.Key, Count = x.Value })
                    .ToList(),
            })
            .OrderByDescending(f => f.Filled)
            .ToList();

        return (total, fields, truncated);
    }

    /// <summary>
    /// Distribuição dos valores de UM campo customizado entre os leads do período
    /// (ex.: campo "Origem" → Instagram 42, Facebook 25, Org 12…). Devolve o top N por
    /// contagem; o restante vira o bucket "Outros". Base do KPI custom tipo gráfico.
    /// </summary>
    public async Task<List<DTOs.Response.KpiBreakdownItemDto>> ComputeBreakdownAsync(
        int clinicId, int? unitId, JsonElement config, DateTime from, DateTime to,
        int topN = 12, string? responsibleUser = null, CancellationToken ct = default)
    {
        const int MaxScan = 8000;
        from = AsUtc(from); to = AsUtc(to);

        var p = ParseConfig(config);
        if (p.FieldId is null && string.IsNullOrWhiteSpace(p.FieldCode))
            return new();

        // Filtra por CreatedAt (não UpdatedAt): origem é um atributo de CRIAÇÃO do lead
        // (vem do anúncio/formulário), não preenchido depois. Usar UpdatedAt quebrava o
        // filtro "por dia" porque sync/webhook bumpam UpdatedAt em todos os leads — um
        // re-sync jogava todo mundo para "hoje". Com o CreatedAt já corrigido (vem da
        // data real da Kommo), a quebra por período fica correta.
        var q = _db.Leads.AsNoTracking()
            .ExcludeDeleted()
            .Where(l => l.TenantId == clinicId && l.CreatedAt >= from && l.CreatedAt <= to
                        && l.CustomFieldsJson != null);
        if (unitId.HasValue)
            q = q.Where(l => l.UnitId == unitId.Value);
        q = await ResponsibleUserFilter.ApplyAsync(q, responsibleUser, ct);

        var jsons = await q.OrderByDescending(l => l.CreatedAt)
            .Take(MaxScan).Select(l => l.CustomFieldsJson!).ToListAsync(ct);

        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var json in jsons)
        {
            var v = ExtractFieldValue(json, p.FieldId, p.FieldCode);
            if (string.IsNullOrWhiteSpace(v)) continue;
            var key = v.Trim();
            counts[key] = counts.GetValueOrDefault(key, 0) + 1;
        }

        var ordered = counts.OrderByDescending(x => x.Value).ToList();
        var top = ordered.Take(topN)
            .Select(x => new DTOs.Response.KpiBreakdownItemDto { Label = x.Key, Value = x.Value })
            .ToList();
        var rest = ordered.Skip(topN).Sum(x => x.Value);
        if (rest > 0)
            top.Add(new DTOs.Response.KpiBreakdownItemDto { Label = "Outros", Value = rest });

        return top;
    }

    /// <summary>
    /// Perfil avançado do lead: idade média por desfecho (contato/agendou/compareceu/fechou/
    /// faltou), alertas de agendamento próximo, ranking de doutor responsável e contagem por
    /// desfecho. Reaproveita os predicados de etapa do dashboard (LeadStages).
    /// </summary>
    public async Task<DTOs.Dashboard.LeadProfileAnalyticsDto> ComputeLeadProfileAsync(
        int clinicId, int? unitId, DateTime from, DateTime to, int upcomingDays = 7, CancellationToken ct = default)
    {
        const int MaxScan = 8000;
        from = AsUtc(from); to = AsUtc(to);
        var now = DateTime.UtcNow;
        var windowEnd = now.AddDays(Math.Clamp(upcomingDays, 1, 60));

        var q = _db.Leads.AsNoTracking()
            .ExcludeDeleted()
            .Where(l => l.TenantId == clinicId && l.CreatedAt >= from && l.CreatedAt <= to);
        if (unitId.HasValue) q = q.Where(l => l.UnitId == unitId.Value);

        var rows = await q.OrderByDescending(l => l.CreatedAt).Take(MaxScan)
            .Select(l => new
            {
                l.Id, l.Name, l.Phone, l.CurrentStage, l.AttendanceStatus,
                l.AppointmentScheduledAt, l.CustomFieldsJson,
            })
            .ToListAsync(ct);

        // Mapeamento de campos escolhido pelo analista (id do campo); se não houver, casa por nome.
        var profileCfg = unitId.HasValue
            ? await GetLeadProfileConfigAsync(unitId.Value, ct)
            : new LeadProfileFields();
        var cfgBirth = profileCfg.BirthdateFieldId;
        var cfgAppt = profileCfg.AppointmentFieldId;
        var cfgDoctor = profileCfg.DoctorFieldId;

        var ageSum = new Dictionary<string, double>();
        var ageCount = new Dictionary<string, int>();
        void AddAge(string seg, double age)
        {
            ageSum[seg] = ageSum.GetValueOrDefault(seg) + age;
            ageCount[seg] = ageCount.GetValueOrDefault(seg) + 1;
        }

        var outcomes = new Dictionary<string, int>();
        void Inc(string seg) => outcomes[seg] = outcomes.GetValueOrDefault(seg) + 1;

        var doctors = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var upcoming = new List<DTOs.Dashboard.UpcomingApptDto>();

        foreach (var l in rows)
        {
            var stage = l.CurrentStage;
            var att = l.AttendanceStatus;
            var isAgendou = LeadStages.HasAppointmentRecord(stage);
            var isCompareceu = att == LeadStages.AttendedCompareceu;
            var isFechou = stage == LeadStages.FechouTratamento || stage == LeadStages.EmTratamento;
            var isFaltou = stage == LeadStages.Faltou || att == LeadStages.AttendedFaltou;

            Inc("contato");
            if (isAgendou) Inc("agendou");
            if (isCompareceu) Inc("compareceu");
            if (isFechou) Inc("fechou");
            if (isFaltou) Inc("faltou");

            var cf = l.CustomFieldsJson;

            var birthRaw = ExtractField(cf, cfgBirth, n => n.Contains("nascimento") || n.Contains("birth"));
            if (TryAge(birthRaw, now) is double age)
            {
                AddAge("contato", age);
                if (isAgendou) AddAge("agendou", age);
                if (isCompareceu) AddAge("compareceu", age);
                if (isFechou) AddAge("fechou", age);
                if (isFaltou) AddAge("faltou", age);
            }

            var doc = ExtractField(cf, cfgDoctor, n => n.Contains("respons") || n.Contains("doutor") || n.Contains("doctor"));
            if (!string.IsNullOrWhiteSpace(doc))
            {
                var key = doc.Trim();
                doctors[key] = doctors.GetValueOrDefault(key) + 1;
            }

            DateTime? appt = l.AppointmentScheduledAt;
            if (appt is null)
            {
                var apptStr = ExtractField(cf, cfgAppt, n => n.Contains("agendamento"));
                if (!string.IsNullOrWhiteSpace(apptStr) && TryParseDate(apptStr, out var d)) appt = d;
            }
            if (appt is DateTime ap)
            {
                var apUtc = AsUtc(ap);
                if (apUtc >= now && apUtc <= windowEnd && !isFaltou && !isFechou)
                    upcoming.Add(new DTOs.Dashboard.UpcomingApptDto
                    {
                        LeadId = l.Id,
                        Name = l.Name ?? "",
                        Phone = l.Phone,
                        ScheduledAt = apUtc,
                        DaysUntil = (int)Math.Max(0, Math.Ceiling((apUtc - now).TotalDays)),
                    });
            }
        }

        DTOs.Dashboard.AgeStatDto Age(string seg) => new()
        {
            Avg = ageCount.GetValueOrDefault(seg) > 0 ? Math.Round(ageSum[seg] / ageCount[seg], 1) : 0,
            Count = ageCount.GetValueOrDefault(seg),
        };

        return new DTOs.Dashboard.LeadProfileAnalyticsDto
        {
            TotalLeads = rows.Count,
            Age = new()
            {
                Overall = Age("contato"),
                Agendou = Age("agendou"),
                Compareceu = Age("compareceu"),
                Fechou = Age("fechou"),
                Faltou = Age("faltou"),
            },
            Upcoming = upcoming.OrderBy(u => u.ScheduledAt).Take(50).ToList(),
            Doctors = doctors.OrderByDescending(x => x.Value).Take(10)
                .Select(x => new DTOs.Dashboard.LabelCountDto { Label = x.Key, Count = x.Value }).ToList(),
            Outcomes = new()
            {
                Contato = outcomes.GetValueOrDefault("contato"),
                Agendou = outcomes.GetValueOrDefault("agendou"),
                Compareceu = outcomes.GetValueOrDefault("compareceu"),
                Fechou = outcomes.GetValueOrDefault("fechou"),
                Faltou = outcomes.GetValueOrDefault("faltou"),
            },
        };
    }

    /// <summary>
    /// Leads com agendamento nos próximos N dias (independente de quando foram criados) — pro
    /// sino de notificação global. Consulta direta em AppointmentScheduledAt (SQL, rápido).
    /// </summary>
    public async Task<List<DTOs.Dashboard.UpcomingApptDto>> UpcomingAppointmentsAsync(
        int clinicId, int? unitId, int days, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var end = now.AddDays(Math.Clamp(days, 1, 60));

        var q = _db.Leads.AsNoTracking()
            .ExcludeDeleted()
            .Where(l => l.TenantId == clinicId
                && l.AppointmentScheduledAt != null
                && l.AppointmentScheduledAt >= now
                && l.AppointmentScheduledAt <= end
                && (l.CurrentStage == LeadStages.AgendadoSemPagamento
                    || l.CurrentStage == LeadStages.AgendadoComPagamento));
        if (unitId.HasValue) q = q.Where(l => l.UnitId == unitId.Value);

        var rows = await q.OrderBy(l => l.AppointmentScheduledAt).Take(50)
            .Select(l => new { l.Id, l.Name, l.Phone, l.AppointmentScheduledAt })
            .ToListAsync(ct);

        return rows.Select(l => new DTOs.Dashboard.UpcomingApptDto
        {
            LeadId = l.Id,
            Name = l.Name ?? "",
            Phone = l.Phone,
            ScheduledAt = l.AppointmentScheduledAt!.Value,
            DaysUntil = (int)Math.Max(0, Math.Ceiling((l.AppointmentScheduledAt!.Value - now).TotalDays)),
        }).ToList();
    }

    /// <summary>Valor do primeiro campo cujo nome (lowercase) casa com o predicado.</summary>
    /// <summary>
    /// Dado o conjunto de leads que ENTRARAM nas etapas <paramref name="stageIds"/> dentro do
    /// período (com a contagem de linhas no período por lead em <paramref name="inPeriodCountByLead"/>),
    /// devolve os leadIds que são RECLASSIFICAÇÃO: já tinham entrado nessas etapas ANTES do
    /// período (total de linhas no histórico &gt; linhas no período). Espelha a mesma regra do
    /// KpiBreakdownsAsync/DashboardOverview — usada pra alinhar o número grande e o drill.
    /// </summary>
    private async Task<HashSet<int>> LoadReclassifiedLeadIdsAsync(
        List<int> stageIds, Dictionary<int, int> inPeriodCountByLead, CancellationToken ct)
    {
        var leadIds = inPeriodCountByLead.Keys.ToList();
        if (leadIds.Count == 0) return new();
        // Só linhas non-legacy: linhas legacy são snapshots do sync (etapa atual), não
        // entradas reais — se contadas, viravam reclassificação falsa (ver KpiBreakdownsAsync).
        var totalByLead = await _db.LeadStageHistories.AsNoTracking()
            .Where(h => stageIds.Contains(h.StageId) && leadIds.Contains(h.LeadId)
                && h.EntrySource != LeadStageHistory.SourceLegacy)
            .GroupBy(h => h.LeadId)
            .Select(g => new { LeadId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.LeadId, x => x.Count, ct);
        var reclass = new HashSet<int>();
        foreach (var (leadId, inPeriod) in inPeriodCountByLead)
            if (totalByLead.GetValueOrDefault(leadId, 0) > inPeriod) reclass.Add(leadId);
        return reclass;
    }

    /// <summary>Todos os campos customizados PREENCHIDOS do lead (nome + valor legível), na ordem
    /// do CustomFieldsJson. Usado no drill-down pra listar "campos preenchidos" do lead.</summary>
    private static List<DTOs.Kpi.KpiLeadFieldDto> ExtractFilledFields(string? json)
    {
        var outList = new List<DTOs.Kpi.KpiLeadFieldDto>();
        if (string.IsNullOrWhiteSpace(json)) return outList;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return outList;
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                if (el.ValueKind != JsonValueKind.Object) continue;
                if (!el.TryGetProperty("field_name", out var n) || n.ValueKind != JsonValueKind.String) continue;
                var name = n.GetString();
                if (string.IsNullOrWhiteSpace(name)) continue;
                if (!el.TryGetProperty("value", out var v)) continue;
                string? value = v.ValueKind switch
                {
                    JsonValueKind.String => v.GetString(),
                    JsonValueKind.Number => v.GetRawText(),
                    JsonValueKind.True => "Sim",
                    JsonValueKind.False => "Não",
                    _ => null,
                };
                if (string.IsNullOrWhiteSpace(value)) continue;
                outList.Add(new DTOs.Kpi.KpiLeadFieldDto { Name = name.Trim(), Value = value.Trim() });
            }
        }
        catch (JsonException) { /* ignora */ }
        return outList;
    }

    private static string? ExtractFieldByName(string? json, Func<string, bool> nameMatches)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                if (el.ValueKind != JsonValueKind.Object) continue;
                if (!el.TryGetProperty("field_name", out var n) || n.ValueKind != JsonValueKind.String) continue;
                var name = n.GetString();
                if (string.IsNullOrWhiteSpace(name) || !nameMatches(name.ToLowerInvariant())) continue;
                if (el.TryGetProperty("value", out var v))
                {
                    if (v.ValueKind == JsonValueKind.String) return v.GetString();
                    if (v.ValueKind == JsonValueKind.Number) return v.GetRawText();
                }
                return null;
            }
        }
        catch (JsonException) { /* ignora */ }
        return null;
    }

    // ── Consultas DO DIA (por data da consulta) ──────────────────────────────
    /// <summary>
    /// Lista as consultas cuja DATA (AppointmentScheduledAt, com fallback no
    /// CustomFieldsJson) cai em [from, to], já com o desfecho classificado.
    /// Alimenta tanto o card "Consultas" (números) quanto a faixa de alerta do dia.
    /// </summary>
    private async Task<List<DTOs.Dashboard.ConsultaDiaItemDto>> LoadConsultasDoDiaAsync(
        int clinicId, int? unitId, LeadProfileFields profile, DateTime from, DateTime to, CancellationToken ct)
    {
        const int MaxScan = 8000;
        from = AsUtc(from); to = AsUtc(to);
        var rows = await _db.Leads.AsNoTracking()
            .ExcludeDeleted()
            .Where(l => l.TenantId == clinicId
                && (!unitId.HasValue || l.UnitId == unitId.Value)
                && (l.AppointmentScheduledAt != null
                    || (l.CustomFieldsJson != null && profile.AppointmentFieldId != null)))
            .Select(l => new { l.Name, l.Phone, l.AppointmentScheduledAt, l.CustomFieldsJson, l.LeadType, l.CurrentStage, l.AttendanceStatus })
            .Take(MaxScan)
            .ToListAsync(ct);

        var items = new List<DTOs.Dashboard.ConsultaDiaItemDto>();
        foreach (var l in rows)
        {
            DateTime? appt = l.AppointmentScheduledAt;
            if (appt is null)
            {
                var apptStr = ExtractField(l.CustomFieldsJson, profile.AppointmentFieldId, n => n.Contains("agendamento"));
                if (!string.IsNullOrWhiteSpace(apptStr) && TryParseDate(apptStr, out var d)) appt = d;
            }
            if (appt is null) continue;
            var apUtc = AsUtc(appt.Value);
            if (apUtc < from || apUtc > to) continue;
            items.Add(new DTOs.Dashboard.ConsultaDiaItemDto
            {
                Name = l.Name,
                Phone = l.Phone,
                When = apUtc,
                Tipo = !string.IsNullOrEmpty(l.LeadType) && l.LeadType.Contains("resgate", StringComparison.OrdinalIgnoreCase)
                    ? "resgate" : "cadastro",
                Outcome = ClassifyConsultaOutcome(l.CurrentStage, l.AttendanceStatus),
            });
        }
        return items.OrderBy(i => i.When).ToList();
    }

    /// <summary>compareceu / faltou / aguardando — a partir da etapa atual e do AttendanceStatus.</summary>
    private static string ClassifyConsultaOutcome(string? currentStage, string? attendance)
    {
        if (currentStage == LeadStages.Faltou
            || string.Equals(attendance, LeadStages.AttendedFaltou, StringComparison.OrdinalIgnoreCase))
            return "faltou";
        if (string.Equals(attendance, LeadStages.AttendedCompareceu, StringComparison.OrdinalIgnoreCase)
            || currentStage is LeadStages.EmTratamento or LeadStages.FechouTratamento or LeadStages.NaoFechouTratamento)
            return "compareceu";
        return "aguardando";
    }

    /// <summary>Versão pública pro endpoint da faixa de hoje: carrega o perfil e lista as consultas do dia.</summary>
    public async Task<List<DTOs.Dashboard.ConsultaDiaItemDto>> ConsultasDoDiaAsync(
        int clinicId, int? unitId, DateTime from, DateTime to, CancellationToken ct = default)
    {
        var profile = unitId.HasValue ? await GetLeadProfileConfigAsync(unitId.Value, ct) : new LeadProfileFields();
        return await LoadConsultasDoDiaAsync(clinicId, unitId, profile, from, to, ct);
    }

    /// <summary>
    /// Drill-down do card "Consultas": lista os LEADS cuja DATA DA CONSULTA cai no
    /// período — o mesmo conjunto que alimenta o número do card (do_dia), pra clicar
    /// no número e ver exatamente as consultas do dia (não "quantas a SDR marcou").
    /// </summary>
    public async Task<(List<DTOs.Kpi.KpiLeadDto> Items, int Total, bool Truncated)> ConsultasDoDiaLeadsAsync(
        int clinicId, int? unitId, DateTime from, DateTime to, int limit, CancellationToken ct = default)
    {
        const int MaxScan = 8000;
        from = AsUtc(from); to = AsUtc(to);
        var profile = unitId.HasValue ? await GetLeadProfileConfigAsync(unitId.Value, ct) : new LeadProfileFields();

        var rows = await _db.Leads.AsNoTracking()
            .ExcludeDeleted()
            .Where(l => l.TenantId == clinicId
                && (!unitId.HasValue || l.UnitId == unitId.Value)
                && (l.AppointmentScheduledAt != null
                    || (l.CustomFieldsJson != null && profile.AppointmentFieldId != null)))
            .Select(l => new
            {
                l.Id, l.ExternalId, l.Name, l.Phone, l.Source, l.Channel, l.CurrentStage, l.CurrentStageId,
                l.LeadType, l.HasAppointment, l.HasPayment, l.CreatedAt, l.AppointmentScheduledAt,
                l.ConsultationValue, l.ClosedTreatment, l.CustomFieldsJson,
            })
            .Take(MaxScan)
            .ToListAsync(ct);

        var inRange = rows
            .Select(l => new { l, Appt = ResolveApptDate(l.AppointmentScheduledAt, l.CustomFieldsJson, profile) })
            .Where(x => x.Appt is not null)
            .Select(x => new { x.l, Appt = AsUtc(x.Appt!.Value) })
            .Where(x => x.Appt >= from && x.Appt <= to)
            .OrderBy(x => x.Appt)
            .ToList();

        var total = inRange.Count;
        var truncated = total > limit;
        var items = inRange.Take(limit).Select(x =>
        {
            var cf = x.l.CustomFieldsJson;
            return new DTOs.Kpi.KpiLeadDto
            {
                Id = x.l.Id,
                ExternalId = x.l.ExternalId,
                Name = x.l.Name,
                Phone = x.l.Phone,
                Source = x.l.Source,
                Channel = x.l.Channel,
                CurrentStage = x.l.CurrentStage,
                CurrentStageId = x.l.CurrentStageId,
                LeadType = x.l.LeadType,
                HasAppointment = x.l.HasAppointment,
                HasPayment = x.l.HasPayment,
                CreatedAt = x.l.CreatedAt,
                AppointmentAt = x.Appt,
                ConsultationValue = x.l.ConsultationValue,
                ClosedTreatment = x.l.ClosedTreatment,
                MotivoNaoAgendamento = ExtractFieldByName(cf, n => n.Contains("motivo") && n.Contains("agendamento")),
                TratamentoFechado = ExtractFieldByName(cf, n => n.Contains("tratamento") && n.Contains("fechad")),
                Qualificacao = ExtractFieldByName(cf, n => n.Contains("qualifica")),
                OrigemCustom = ExtractFieldByName(cf, n => n.Contains("origem")),
            };
        }).ToList();

        return (items, total, truncated);
    }

    /// <summary>Data da consulta: prefere a coluna sincronizada; cai no CustomFieldsJson.</summary>
    private DateTime? ResolveApptDate(DateTime? column, string? customFieldsJson, LeadProfileFields profile)
    {
        if (column is not null) return column;
        var s = ExtractField(customFieldsJson, profile.AppointmentFieldId, n => n.Contains("agendamento"));
        return !string.IsNullOrWhiteSpace(s) && TryParseDate(s, out var d) ? d : null;
    }

    /// <summary>Valor do campo: prefere o id configurado; senão casa por nome.</summary>
    private static string? ExtractField(string? json, long? fieldId, Func<string, bool> nameMatches)
        => fieldId.HasValue ? ExtractFieldValue(json ?? "[]", fieldId, null) : ExtractFieldByName(json, nameMatches);

    // ── Classificação Cadastro × Resgate ──────────────────────────────────────────
    // Fonte da verdade: a coluna LeadType (mesma do número grande do card). Cai pro custom
    // field "Tipo" mapeado só quando LeadType vem vazio. Cadastro pega vazio/"cadastro"/
    // "novo"; Resgate exige "resgate". Espelha o ResolveTipo do KpiBreakdownsAsync pra o
    // drill bater com o número do card.
    private static string? ResolveTipoField(string? cf, string? leadType, long? tipoFieldId)
    {
        if (!string.IsNullOrWhiteSpace(leadType)) return leadType.Trim();
        return ExtractField(cf, tipoFieldId, n => n == "tipo")?.Trim();
    }
    private static bool IsCadastroTipo(string? t) =>
        string.IsNullOrEmpty(t) || t.Contains("cadastro", StringComparison.OrdinalIgnoreCase) || t.Contains("novo", StringComparison.OrdinalIgnoreCase);
    private static bool IsResgateTipo(string? t) =>
        !string.IsNullOrEmpty(t) && t.Contains("resgate", StringComparison.OrdinalIgnoreCase);

    /// <summary>Tenta converter um valor de campo customizado em decimal (aceita "1.500,00" e "1500.00").</summary>
    private static decimal? TryParseDecimal(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var s = raw.Trim();
        if (decimal.TryParse(s, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var v1)) return v1;
        if (decimal.TryParse(s, System.Globalization.NumberStyles.Any, new System.Globalization.CultureInfo("pt-BR"), out var v2)) return v2;
        return null;
    }

    /// <summary>Idade a partir de uma data de nascimento (string).</summary>
    private static double? TryAge(string? raw, DateTime now)
    {
        if (string.IsNullOrWhiteSpace(raw) || !TryParseDate(raw, out var birth)) return null;
        var age = now.Year - birth.Year;
        if (now < birth.AddYears(age)) age--;
        return age is < 0 or > 120 ? null : age;
    }

    /// <summary>Lê os ids de campo escolhidos pra Perfil do Lead (nascimento/agendamento/doutor).</summary>
    public async Task<LeadProfileFields> GetLeadProfileConfigAsync(
        int unitId, CancellationToken ct = default)
    {
        var keys = new[] {
            ProfileBirthdateKey, ProfileAppointmentKey, ProfileDoctorKey,
            ProfileOrigemKey, ProfileMotivoKey, ProfileFisioKey,
            ProfileValorTratKey, ProfileValorConsultaKey,
            ProfileTratFechadoKey, ProfileQualificacaoKey,
            ProfileTipoKey, ProfileTipoAgendamentoKey, ProfileTipoTratamentoKey,
        };
        var rows = await _db.KpiConfigurations.AsNoTracking()
            .Where(k => k.UnitId == unitId && keys.Contains(k.KpiKey))
            .ToListAsync(ct);

        long? FieldOf(string key)
        {
            var r = rows.FirstOrDefault(x => x.KpiKey == key);
            if (r is null || string.IsNullOrWhiteSpace(r.ConfigJson)) return null;
            try { return ParseConfig(JsonSerializer.Deserialize<JsonElement>(r.ConfigJson)).FieldId; }
            catch { return null; }
        }

        return new LeadProfileFields
        {
            BirthdateFieldId = FieldOf(ProfileBirthdateKey),
            AppointmentFieldId = FieldOf(ProfileAppointmentKey),
            DoctorFieldId = FieldOf(ProfileDoctorKey),
            OrigemFieldId = FieldOf(ProfileOrigemKey),
            MotivoNaoAgendamentoFieldId = FieldOf(ProfileMotivoKey),
            FisioterapeutaFieldId = FieldOf(ProfileFisioKey),
            ValorTratamentoFieldId = FieldOf(ProfileValorTratKey),
            ValorConsultaFieldId = FieldOf(ProfileValorConsultaKey),
            TratamentoFechadoFieldId = FieldOf(ProfileTratFechadoKey),
            QualificacaoFieldId = FieldOf(ProfileQualificacaoKey),
            TipoFieldId = FieldOf(ProfileTipoKey),
            TipoAgendamentoFieldId = FieldOf(ProfileTipoAgendamentoKey),
            TipoTratamentoFieldId = FieldOf(ProfileTipoTratamentoKey),
        };
    }

    /// <summary>Salva (upsert) os ids de campo do Perfil do Lead. Id nulo = limpa (volta pro nome).</summary>
    public async Task SaveLeadProfileConfigAsync(
        int unitId, int clinicId, LeadProfileFields f,
        string? email, CancellationToken ct = default)
    {
        static string Cfg(long? id) => id.HasValue ? $"{{\"fieldId\":{id.Value}}}" : "{}";
        var items = new[]
        {
            new KpiSaveItem(ProfileBirthdateKey, KpiSourceTypes.CustomFieldCount, Cfg(f.BirthdateFieldId)),
            new KpiSaveItem(ProfileAppointmentKey, KpiSourceTypes.CustomFieldCount, Cfg(f.AppointmentFieldId)),
            new KpiSaveItem(ProfileDoctorKey, KpiSourceTypes.CustomFieldCount, Cfg(f.DoctorFieldId)),
            new KpiSaveItem(ProfileOrigemKey, KpiSourceTypes.CustomFieldCount, Cfg(f.OrigemFieldId)),
            new KpiSaveItem(ProfileMotivoKey, KpiSourceTypes.CustomFieldCount, Cfg(f.MotivoNaoAgendamentoFieldId)),
            new KpiSaveItem(ProfileFisioKey, KpiSourceTypes.CustomFieldCount, Cfg(f.FisioterapeutaFieldId)),
            new KpiSaveItem(ProfileValorTratKey, KpiSourceTypes.CustomFieldCount, Cfg(f.ValorTratamentoFieldId)),
            new KpiSaveItem(ProfileValorConsultaKey, KpiSourceTypes.CustomFieldCount, Cfg(f.ValorConsultaFieldId)),
            new KpiSaveItem(ProfileTratFechadoKey, KpiSourceTypes.CustomFieldCount, Cfg(f.TratamentoFechadoFieldId)),
            new KpiSaveItem(ProfileQualificacaoKey, KpiSourceTypes.CustomFieldCount, Cfg(f.QualificacaoFieldId)),
            new KpiSaveItem(ProfileTipoKey, KpiSourceTypes.CustomFieldCount, Cfg(f.TipoFieldId)),
            new KpiSaveItem(ProfileTipoAgendamentoKey, KpiSourceTypes.CustomFieldCount, Cfg(f.TipoAgendamentoFieldId)),
            new KpiSaveItem(ProfileTipoTratamentoKey, KpiSourceTypes.CustomFieldCount, Cfg(f.TipoTratamentoFieldId)),
        };
        await SaveAsync(unitId, clinicId, items, email, ct);
    }

    /// <summary>Parse tolerante de data: yyyy-MM-dd / ISO / pt-BR / unix (segundos ou ms).</summary>
    private static bool TryParseDate(string raw, out DateTime date)
    {
        date = default;
        raw = raw.Trim();
        if (raw.Length == 0) return false;
        if (long.TryParse(raw, out var num) && num > 0)
        {
            try
            {
                date = num > 99999999999L
                    ? DateTimeOffset.FromUnixTimeMilliseconds(num).UtcDateTime
                    : DateTimeOffset.FromUnixTimeSeconds(num).UtcDateTime;
                return true;
            }
            catch { return false; }
        }
        const DateTimeStyles styles = DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal;
        if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, styles, out date)) return true;
        return DateTime.TryParse(raw, new CultureInfo("pt-BR"), styles, out date);
    }

    private sealed class FieldAgg
    {
        public string Name = "";
        public string? Code;
        public string Type = "text";
        public int Filled;
        public Dictionary<string, int> Values = new();
    }

    // ─── Parsing ─────────────────────────────────────────────────────────────

    private record ParsedConfig(List<int> StageIds, long? FieldId, string? FieldCode, List<string> MatchValues);

    private static ParsedConfig ParseConfig(JsonElement config)
    {
        var stageIds = new List<int>();
        long? fieldId = null;
        string? fieldCode = null;
        var matchValues = new List<string>();

        if (config.ValueKind == JsonValueKind.Object)
        {
            if (config.TryGetProperty("stageIds", out var s) && s.ValueKind == JsonValueKind.Array)
                foreach (var el in s.EnumerateArray())
                    if (TryGetInt(el, out var id)) stageIds.Add(id);

            if (config.TryGetProperty("fieldId", out var f) && TryGetLong(f, out var fid))
                fieldId = fid;

            if (config.TryGetProperty("fieldCode", out var fc) && fc.ValueKind == JsonValueKind.String)
                fieldCode = fc.GetString();

            if (config.TryGetProperty("matchValues", out var mv) && mv.ValueKind == JsonValueKind.Array)
                foreach (var el in mv.EnumerateArray())
                    if (el.ValueKind == JsonValueKind.String) matchValues.Add(el.GetString() ?? "");
        }

        return new ParsedConfig(stageIds, fieldId, fieldCode, matchValues);
    }

    /// <summary>Extrai o "value" de um campo do CustomFieldsJson casando por field_id ou field_code.</summary>
    private static string? ExtractFieldValue(string customFieldsJson, long? fieldId, string? fieldCode)
    {
        try
        {
            using var doc = JsonDocument.Parse(customFieldsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                if (el.ValueKind != JsonValueKind.Object) continue;

                var idMatch = fieldId is not null
                    && el.TryGetProperty("field_id", out var fid)
                    && TryGetLong(fid, out var idv) && idv == fieldId.Value;

                var codeMatch = !string.IsNullOrWhiteSpace(fieldCode)
                    && el.TryGetProperty("field_code", out var fc)
                    && fc.ValueKind == JsonValueKind.String
                    && string.Equals(fc.GetString(), fieldCode, StringComparison.OrdinalIgnoreCase);

                if (idMatch || codeMatch)
                {
                    if (el.TryGetProperty("value", out var val) && val.ValueKind == JsonValueKind.String)
                        return val.GetString();
                    if (el.TryGetProperty("value", out var valN) && valN.ValueKind == JsonValueKind.Number)
                        return valN.GetRawText();
                    return null;
                }
            }
        }
        catch (JsonException) { /* json malformado — ignora */ }
        return null;
    }

    /// <summary>
    /// Normaliza para UTC. O Npgsql (sem legacy timestamp) recusa DateTime com
    /// Kind=Unspecified ao comparar com colunas timestamptz — as datas vindas da query
    /// string chegam Unspecified, então precisam ser marcadas como UTC antes do WHERE.
    /// </summary>
    private static DateTime AsUtc(DateTime d) =>
        d.Kind == DateTimeKind.Utc ? d
        : d.Kind == DateTimeKind.Local ? d.ToUniversalTime()
        : DateTime.SpecifyKind(d, DateTimeKind.Utc);

    private static bool TryGetInt(JsonElement el, out int value)
    {
        value = 0;
        if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out value)) return true;
        if (el.ValueKind == JsonValueKind.String && int.TryParse(el.GetString(), out value)) return true;
        return false;
    }

    private static bool TryGetLong(JsonElement el, out long value)
    {
        value = 0;
        if (el.ValueKind == JsonValueKind.Number && el.TryGetInt64(out value)) return true;
        if (el.ValueKind == JsonValueKind.String && long.TryParse(el.GetString(), out value)) return true;
        return false;
    }

    /// <summary>Parse tolerante de número (aceita "1.234,56" pt-BR, "1234.56", "R$ 1.200").</summary>
    private static bool TryParseNumber(string raw, out double value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(raw)) return false;

        // mantém só dígitos, separadores e sinal
        var cleaned = new string(raw.Where(c => char.IsDigit(c) || c is '.' or ',' or '-').ToArray());
        if (cleaned.Length == 0) return false;

        var lastComma = cleaned.LastIndexOf(',');
        var lastDot = cleaned.LastIndexOf('.');
        // o separador mais à direita é o decimal; o outro é separador de milhar
        if (lastComma > lastDot)
            cleaned = cleaned.Replace(".", "").Replace(',', '.');
        else
            cleaned = cleaned.Replace(",", "");

        return double.TryParse(cleaned, NumberStyles.Any, CultureInfo.InvariantCulture, out value);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // CROSS-ANALYSIS — Sexo × desfecho, Tratamento indicado, Motivos, etc.
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Análises cruzadas dos campos customizados — Sexo × desfecho (agendou/
    /// compareceu/fechou/faltou) + distribuições de Tratamento indicado,
    /// Tratamento fechado, Motivo do não agendamento, Profissão, Origem,
    /// Responsável agendamento, Qualificação. Tudo numa varredura só.
    /// </summary>
    public async Task<DTOs.Dashboard.CustomFieldsCrossAnalysisDto>
        CustomFieldsCrossAnalysisAsync(int clinicId, int? unitId, DateTime from, DateTime to,
            int topPerField = 12, CancellationToken ct = default)
    {
        const int MaxScan = 10_000;
        from = AsUtc(from); to = AsUtc(to);

        var q = _db.Leads.AsNoTracking()
            .ExcludeDeleted()
            .Where(l => l.TenantId == clinicId
                        && l.UpdatedAt >= from && l.UpdatedAt <= to
                        && l.CustomFieldsJson != null);
        if (unitId.HasValue)
            q = q.Where(l => l.UnitId == unitId.Value);

        var rows = await q
            .OrderByDescending(l => l.UpdatedAt)
            .Take(MaxScan)
            .Select(l => new { l.CurrentStage, l.AttendanceStatus, l.CustomFieldsJson })
            .ToListAsync(ct);

        // Agregadores
        var sexoBucket = new Dictionary<string, DTOs.Dashboard.SexoOutcomeRowDto>(StringComparer.OrdinalIgnoreCase);
        var tratamentoIndicado = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var tratamentoFechado = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var motivoNaoAgendamento = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var profissao = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var origem = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var responsavel = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var qualificacao = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        // Cruzamentos × desfecho
        var atendenteOutcome = new Dictionary<string, DTOs.Dashboard.OutcomeRowDto>(StringComparer.OrdinalIgnoreCase);
        var origemOutcome = new Dictionary<string, DTOs.Dashboard.OutcomeRowDto>(StringComparer.OrdinalIgnoreCase);
        var qualificacaoOutcome = new Dictionary<string, DTOs.Dashboard.OutcomeRowDto>(StringComparer.OrdinalIgnoreCase);
        // Motivo não-agendamento × atendente
        var motivoByAtendente = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        static void BumpOutcome(Dictionary<string, DTOs.Dashboard.OutcomeRowDto> bucket, string key,
            bool agendou, bool compareceu, bool fechou, bool faltou)
        {
            if (!bucket.TryGetValue(key, out var row))
            {
                row = new DTOs.Dashboard.OutcomeRowDto { Label = key };
                bucket[key] = row;
            }
            row.Total++;
            if (agendou) row.Agendou++;
            if (compareceu) row.Compareceu++;
            if (fechou) row.Fechou++;
            if (faltou) row.Faltou++;
        }

        foreach (var l in rows)
        {
            var stage = l.CurrentStage ?? "";
            var att = l.AttendanceStatus;
            var agendou = LeadStages.HasAppointmentRecord(stage);
            var compareceu = att == LeadStages.AttendedCompareceu;
            var fechou = stage == LeadStages.FechouTratamento || stage == LeadStages.EmTratamento;
            var faltou = stage == LeadStages.Faltou || att == LeadStages.AttendedFaltou;

            var cf = l.CustomFieldsJson;

            // Sexo (radiobutton normalmente — Feminino/Masculino/Outro)
            var sexo = ExtractFieldByName(cf, n => n == "sexo");
            if (!string.IsNullOrWhiteSpace(sexo))
            {
                var key = sexo.Trim();
                if (!sexoBucket.TryGetValue(key, out var row))
                {
                    row = new DTOs.Dashboard.SexoOutcomeRowDto { Sexo = key };
                    sexoBucket[key] = row;
                }
                row.Total++;
                if (agendou) row.Agendou++;
                if (compareceu) row.Compareceu++;
                if (fechou) row.Fechou++;
                if (faltou) row.Faltou++;
            }

            // Tratamento indicado (multiselect — pode ter "A, B, C")
            var trIndic = ExtractFieldByName(cf, n => n.Contains("tratamento") && n.Contains("indicad"));
            CountMultiselect(tratamentoIndicado, trIndic);

            // Tratamento fechado
            var trFech = ExtractFieldByName(cf, n => n.Contains("tratamento") && n.Contains("fechad"));
            CountMultiselect(tratamentoFechado, trFech);

            // Motivo do não agendamento
            var motivo = ExtractFieldByName(cf, n => n.Contains("motivo") && n.Contains("agendamento"));
            if (!string.IsNullOrWhiteSpace(motivo))
                motivoNaoAgendamento[motivo.Trim()] = motivoNaoAgendamento.GetValueOrDefault(motivo.Trim()) + 1;

            // Profissão
            var prof = ExtractFieldByName(cf, n => n == "profissão" || n == "profissao");
            if (!string.IsNullOrWhiteSpace(prof))
                profissao[prof.Trim()] = profissao.GetValueOrDefault(prof.Trim()) + 1;

            // Origem
            var orig = ExtractFieldByName(cf, n => n == "origem");
            if (!string.IsNullOrWhiteSpace(orig))
                origem[orig.Trim()] = origem.GetValueOrDefault(orig.Trim()) + 1;

            // Responsável agendamento
            var resp = ExtractFieldByName(cf, n => n.Contains("responsável") && n.Contains("agendamento")
                                                    || n.Contains("responsavel") && n.Contains("agendamento"));
            if (!string.IsNullOrWhiteSpace(resp))
                responsavel[resp.Trim()] = responsavel.GetValueOrDefault(resp.Trim()) + 1;

            // Qualificação do lead
            var qual = ExtractFieldByName(cf, n => n.Contains("qualifica"));
            if (!string.IsNullOrWhiteSpace(qual))
                qualificacao[qual.Trim()] = qualificacao.GetValueOrDefault(qual.Trim()) + 1;

            // ── Cruzamentos × desfecho ──
            if (!string.IsNullOrWhiteSpace(resp))
                BumpOutcome(atendenteOutcome, resp.Trim(), agendou, compareceu, fechou, faltou);
            if (!string.IsNullOrWhiteSpace(orig))
                BumpOutcome(origemOutcome, orig.Trim(), agendou, compareceu, fechou, faltou);
            if (!string.IsNullOrWhiteSpace(qual))
                BumpOutcome(qualificacaoOutcome, qual.Trim(), agendou, compareceu, fechou, faltou);

            // Motivo do não agendamento × Atendente (par)
            if (!string.IsNullOrWhiteSpace(motivo) && !string.IsNullOrWhiteSpace(resp))
            {
                var pairKey = $"{resp.Trim()}|||{motivo.Trim()}";
                motivoByAtendente[pairKey] = motivoByAtendente.GetValueOrDefault(pairKey) + 1;
            }
        }

        return new DTOs.Dashboard.CustomFieldsCrossAnalysisDto
        {
            TotalLeads = rows.Count,
            SexoByOutcome = sexoBucket.Values.OrderByDescending(r => r.Total).ToList(),
            TratamentoIndicado = TopN(tratamentoIndicado, topPerField),
            TratamentoFechado = TopN(tratamentoFechado, topPerField),
            MotivoNaoAgendamento = TopN(motivoNaoAgendamento, topPerField),
            Profissao = TopN(profissao, topPerField),
            Origem = TopN(origem, topPerField),
            ResponsavelAgendamento = TopN(responsavel, topPerField),
            Qualificacao = TopN(qualificacao, topPerField),
            AtendenteByOutcome = atendenteOutcome.Values.OrderByDescending(r => r.Total).Take(topPerField).ToList(),
            OrigemByOutcome = origemOutcome.Values.OrderByDescending(r => r.Total).Take(topPerField).ToList(),
            QualificacaoByOutcome = qualificacaoOutcome.Values.OrderByDescending(r => r.Total).ToList(),
            MotivoByAtendente = motivoByAtendente
                .OrderByDescending(kv => kv.Value)
                .Take(topPerField)
                .Select(kv =>
                {
                    var parts = kv.Key.Split("|||", 2);
                    return new DTOs.Dashboard.PairCountDto
                    {
                        GroupA = parts[0],
                        GroupB = parts.Length > 1 ? parts[1] : "",
                        Count = kv.Value,
                    };
                })
                .ToList(),
        };
    }

    private static void CountMultiselect(Dictionary<string, int> bucket, string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return;
        // multiselect vem salvo como "A, B, C" pelo SerializeCustomFields
        foreach (var part in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            bucket[part] = bucket.GetValueOrDefault(part) + 1;
    }

    private static List<DTOs.Dashboard.ValueCountDto> TopN(Dictionary<string, int> bucket, int n) =>
        bucket.OrderByDescending(kv => kv.Value)
              .Take(n)
              .Select(kv => new DTOs.Dashboard.ValueCountDto { Value = kv.Key, Count = kv.Value })
              .ToList();
}
