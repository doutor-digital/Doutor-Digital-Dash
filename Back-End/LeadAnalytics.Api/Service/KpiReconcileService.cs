using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace LeadAnalytics.Api.Service;

/// <summary>
/// Reconciliação em massa do KPI contra a "fonte da verdade" (CSV exportado dos
/// relatórios da clínica). Caso de uso: SDR moveu lead pra etapa errada/dia errado
/// e a chefe precisa do dashboard batendo com o relatório real.
///
/// Três modos:
///  • tratamentos — CSV "Tratamentos Realizados"; corrige data do FechouTratamento
///                  e marca como "não contar" leads em FechouTratamento que NÃO
///                  estão no CSV.
///  • agendados   — CSV "Cadastro Geral" filtrado por Cliente Agendou?=Sim; corrige
///                  data da entrada em AgendadoSem/ComPagamento e exclui agendados
///                  no banco que não estão no CSV.
///  • compareceu  — CSV "Consultas Comparecidas"; seta Lead.AttendanceStatus
///                  pros leads que de fato compareceram.
///
/// Match: telefone (variantes 8/9/10/11 dígitos) com fallback por nome + data inline.
/// Mesma estratégia do CloudiaCsvImportService — copiei os helpers pra manter este
/// serviço auto-contido (refactor pode ficar pra depois).
/// </summary>
public class KpiReconcileService(AppDbContext db, ILogger<KpiReconcileService> logger)
{
    private readonly AppDbContext _db = db;
    private readonly ILogger<KpiReconcileService> _logger = logger;

    public sealed class ReconcileResult
    {
        public bool DryRun { get; set; }
        public string KpiKey { get; set; } = "";
        public int UnitId { get; set; }
        public int CsvRows { get; set; }
        public int UniqueRows { get; set; }
        public int Matched { get; set; }
        public int Ambiguous { get; set; }
        public int Missed { get; set; }
        /// <summary>Leads achados mas que NÃO têm entrada em LeadStageHistory pra essa etapa.</summary>
        public int MatchedNoHistory { get; set; }
        public int DatesCorrected { get; set; }
        public int ExclusionsAdded { get; set; }
        public int AttendanceMarked { get; set; }
        public long DurationMs { get; set; }
        public List<string> SampleMissed { get; set; } = new();
        public List<string> SampleAmbiguous { get; set; } = new();
        public List<string> SampleNoHistory { get; set; } = new();
        public List<SampleCorrection> SampleCorrections { get; set; } = new();
        public List<SampleExclusion> SampleExclusions { get; set; } = new();
    }

    public sealed class SampleCorrection
    {
        public int LeadId { get; set; }
        public string LeadName { get; set; } = "";
        public DateTime From { get; set; }
        public DateTime To { get; set; }
    }
    public sealed class SampleExclusion
    {
        public int LeadId { get; set; }
        public string LeadName { get; set; } = "";
        public string CurrentStage { get; set; } = "";
    }

    // ─── Tratamentos ─────────────────────────────────────────────────────────

    public async Task<ReconcileResult> ReconcileTratamentosAsync(
        int unitId, int tenantId, Stream csvStream, bool dryRun, CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var r = new ReconcileResult { DryRun = dryRun, KpiKey = "tratamentos", UnitId = unitId };

        var (headers, rows) = ParseCsv(csvStream);
        if (headers.Length == 0) return r;

        // Tratamentos Realizados.csv: ID, Data, Nome, Telefone, Tipo, Origem, Valor, ...
        var iData = IndexOf(headers, "Data");
        var iNome = IndexOf(headers, "Nome");
        var iFone = IndexOf(headers, "Telefone");
        if (iData < 0 || iNome < 0)
        {
            _logger.LogWarning("[reconcile-trat] CSV sem colunas Data/Nome");
            return r;
        }

        // Dedup por telefone (vence a entrada mais recente). Sem telefone, usa key única.
        var byPhone = DedupByPhone(rows, iFone, iData, r);

        // Leads que ESTÃO no CSV (fecharam tratamento de verdade) → vão receber correção de data.
        var matchedLeadIds = new HashSet<int>();
        var corrections = new List<(int leadId, DateTime targetUtc, string leadName)>();

        foreach (var row in byPhone.Values)
        {
            var nome = SafeGet(row, iNome);
            var dataCsv = SafeGet(row, iData);
            var phone = NormPhone(SafeGet(row, iFone));

            var words = NameWords(nome);
            var dates = DateVariants(dataCsv);
            if (string.IsNullOrEmpty(phone) && (words.Count == 0 || dates.Count == 0))
            { r.Missed++; continue; }

            var target = ParseBrDateTimeUtc(dataCsv);
            if (target == null) { r.Missed++; continue; }

            var match = await FindMatchAsync(unitId, phone, words, dates, ct);
            if (match == null) { r.Missed++; if (r.SampleMissed.Count < 10) r.SampleMissed.Add($"{nome} ({dataCsv})"); continue; }
            if (match.Count > 1) { r.Ambiguous++; if (r.SampleAmbiguous.Count < 10) r.SampleAmbiguous.Add($"{nome} ({dataCsv}) → {match.Count} matches"); continue; }

            var lead = match[0];
            if (!matchedLeadIds.Add(lead.Id)) { r.Ambiguous++; continue; }
            r.Matched++;
            corrections.Add((lead.Id, target.Value, lead.Name));
        }

        // Pra cada match, busca a entrada mais recente em FechouTratamento e corrige a data.
        // Histórico inexistente: lead está em outra etapa no Kommo mas a SDR não moveu pra
        // FechouTratamento; nesse caso só conseguimos contabilizar via kpi_overrides + exclusões.
        var leadIdsWithCorrection = corrections.Select(c => c.leadId).ToList();
        var histRows = await _db.LeadStageHistories.AsNoTracking()
            .Where(h => leadIdsWithCorrection.Contains(h.LeadId)
                     && h.StageLabel == LeadStages.FechouTratamento)
            .Select(h => new { h.Id, h.LeadId, h.ChangedAt, h.CorrectedChangedAt })
            .ToListAsync(ct);
        var latestHistByLead = histRows
            .GroupBy(h => h.LeadId)
            .ToDictionary(g => g.Key, g => g
                .OrderByDescending(h => h.CorrectedChangedAt ?? h.ChangedAt)
                .First());

        var historyUpdates = new List<(int historyId, DateTime targetUtc, DateTime fromUtc, string leadName, int leadId)>();
        foreach (var c in corrections)
        {
            if (!latestHistByLead.TryGetValue(c.leadId, out var h))
            {
                r.MatchedNoHistory++;
                if (r.SampleNoHistory.Count < 10) r.SampleNoHistory.Add($"{c.leadName} (id={c.leadId})");
                continue;
            }
            var from = h.CorrectedChangedAt ?? h.ChangedAt;
            if (from.Date == c.targetUtc.Date) continue; // já está no dia certo
            historyUpdates.Add((h.Id, c.targetUtc, from, c.leadName, c.leadId));
        }

        r.DatesCorrected = historyUpdates.Count;
        r.SampleCorrections = historyUpdates.Take(20)
            .Select(u => new SampleCorrection { LeadId = u.leadId, LeadName = u.leadName, From = u.fromUtc, To = u.targetUtc })
            .ToList();

        // Leads em FechouTratamento NO BANCO da unidade que NÃO estão no CSV → não contar.
        var fechouNoBanco = await _db.Leads.AsNoTracking()
            .Where(l => l.UnitId == unitId
                     && l.TenantId == tenantId
                     && l.CurrentStage == LeadStages.FechouTratamento)
            .Select(l => new { l.Id, l.Name, l.CurrentStage })
            .ToListAsync(ct);

        var jaExcluidos = await _db.KpiExclusions.AsNoTracking()
            .Where(e => e.TenantId == tenantId && e.UnitId == unitId && e.KpiKey == "tratamentos")
            .Select(e => e.LeadId)
            .ToListAsync(ct);
        var jaExcluidosSet = jaExcluidos.ToHashSet();

        var toExclude = fechouNoBanco
            .Where(l => !matchedLeadIds.Contains(l.Id) && !jaExcluidosSet.Contains(l.Id))
            .ToList();
        r.ExclusionsAdded = toExclude.Count;
        r.SampleExclusions = toExclude.Take(20)
            .Select(l => new SampleExclusion { LeadId = l.Id, LeadName = l.Name, CurrentStage = l.CurrentStage })
            .ToList();

        if (!dryRun)
        {
            await ApplyHistoryCorrectionsAsync(historyUpdates, ct);
            await AddExclusionsAsync(tenantId, unitId, "tratamentos", toExclude.Select(l => l.Id).ToList(), ct);
        }

        sw.Stop();
        r.DurationMs = sw.ElapsedMilliseconds;
        _logger.LogInformation(
            "[reconcile-trat] {Mode} unit={Unit} csv={Csv} match={Match} corrigidos={Corr} excluidos={Excl} miss={Miss}",
            dryRun ? "DRY-RUN" : "APLICADO", unitId, r.CsvRows, r.Matched, r.DatesCorrected, r.ExclusionsAdded, r.Missed);
        return r;
    }

    // ─── Agendados ───────────────────────────────────────────────────────────

    public async Task<ReconcileResult> ReconcileAgendadosAsync(
        int unitId, int tenantId, Stream csvStream, bool dryRun, CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var r = new ReconcileResult { DryRun = dryRun, KpiKey = "agendados", UnitId = unitId };

        var (headers, rows) = ParseCsv(csvStream);
        if (headers.Length == 0) return r;

        var iNome = IndexOf(headers, "Nome do Cliente", "Nome");
        var iFone = IndexOf(headers, "Telefone");
        var iAgendou = IndexOf(headers, "Cliente Agendou?", "Cliente Agendou");
        var iDataAg = IndexOf(headers, "Data do Agendamento");
        var iDataOrig = IndexOf(headers, "Data Origem", "Data");
        if (iNome < 0 || iAgendou < 0 || iDataAg < 0)
        {
            _logger.LogWarning("[reconcile-ag] CSV sem colunas Nome/Cliente Agendou?/Data do Agendamento");
            return r;
        }

        var byPhone = DedupByPhone(rows, iFone, iDataOrig, r);
        var matchedLeadIds = new HashSet<int>();
        var corrections = new List<(int leadId, DateTime targetUtc, string leadName)>();

        foreach (var row in byPhone.Values)
        {
            var nome = SafeGet(row, iNome);
            var agendou = SafeGet(row, iAgendou).Trim();
            var dataAg = SafeGet(row, iDataAg);
            var phone = NormPhone(SafeGet(row, iFone));

            // Só nos interessam as linhas onde Cliente Agendou? = Sim (com Data do Agendamento)
            if (!string.Equals(agendou, "Sim", StringComparison.OrdinalIgnoreCase)) continue;
            if (string.IsNullOrWhiteSpace(dataAg)) continue;

            var words = NameWords(nome);
            var dates = DateVariants(SafeGet(row, iDataOrig));
            if (string.IsNullOrEmpty(phone) && (words.Count == 0 || dates.Count == 0))
            { r.Missed++; continue; }

            var target = ParseBrDateTimeUtc(dataAg);
            if (target == null) { r.Missed++; continue; }

            var match = await FindMatchAsync(unitId, phone, words, dates, ct);
            if (match == null) { r.Missed++; if (r.SampleMissed.Count < 10) r.SampleMissed.Add($"{nome} ({dataAg})"); continue; }
            if (match.Count > 1) { r.Ambiguous++; if (r.SampleAmbiguous.Count < 10) r.SampleAmbiguous.Add($"{nome} → {match.Count} matches"); continue; }

            var lead = match[0];
            if (!matchedLeadIds.Add(lead.Id)) { r.Ambiguous++; continue; }
            r.Matched++;
            corrections.Add((lead.Id, target.Value, lead.Name));
        }

        // Pega a entrada mais recente em Agendado*/04|05 pra esses leads.
        var leadIdsWithCorrection = corrections.Select(c => c.leadId).ToList();
        var agStages = new[] { LeadStages.AgendadoSemPagamento, LeadStages.AgendadoComPagamento };
        var histRows = await _db.LeadStageHistories.AsNoTracking()
            .Where(h => leadIdsWithCorrection.Contains(h.LeadId) && agStages.Contains(h.StageLabel))
            .Select(h => new { h.Id, h.LeadId, h.ChangedAt, h.CorrectedChangedAt })
            .ToListAsync(ct);
        var latestHistByLead = histRows
            .GroupBy(h => h.LeadId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(h => h.CorrectedChangedAt ?? h.ChangedAt).First());

        var historyUpdates = new List<(int historyId, DateTime targetUtc, DateTime fromUtc, string leadName, int leadId)>();
        foreach (var c in corrections)
        {
            if (!latestHistByLead.TryGetValue(c.leadId, out var h))
            {
                r.MatchedNoHistory++;
                if (r.SampleNoHistory.Count < 10) r.SampleNoHistory.Add($"{c.leadName} (id={c.leadId})");
                continue;
            }
            var from = h.CorrectedChangedAt ?? h.ChangedAt;
            if (from.Date == c.targetUtc.Date) continue;
            historyUpdates.Add((h.Id, c.targetUtc, from, c.leadName, c.leadId));
        }

        r.DatesCorrected = historyUpdates.Count;
        r.SampleCorrections = historyUpdates.Take(20)
            .Select(u => new SampleCorrection { LeadId = u.leadId, LeadName = u.leadName, From = u.fromUtc, To = u.targetUtc })
            .ToList();

        // Leads em Agendado* no banco que NÃO estão no CSV (ou estão com Agendou=Não) → não contar.
        var agNoBanco = await _db.Leads.AsNoTracking()
            .Where(l => l.UnitId == unitId
                     && l.TenantId == tenantId
                     && (l.CurrentStage == LeadStages.AgendadoSemPagamento
                      || l.CurrentStage == LeadStages.AgendadoComPagamento))
            .Select(l => new { l.Id, l.Name, l.CurrentStage })
            .ToListAsync(ct);
        var jaExcluidos = await _db.KpiExclusions.AsNoTracking()
            .Where(e => e.TenantId == tenantId && e.UnitId == unitId && e.KpiKey == "agendados")
            .Select(e => e.LeadId)
            .ToListAsync(ct);
        var jaExcluidosSet = jaExcluidos.ToHashSet();

        var toExclude = agNoBanco
            .Where(l => !matchedLeadIds.Contains(l.Id) && !jaExcluidosSet.Contains(l.Id))
            .ToList();
        r.ExclusionsAdded = toExclude.Count;
        r.SampleExclusions = toExclude.Take(20)
            .Select(l => new SampleExclusion { LeadId = l.Id, LeadName = l.Name, CurrentStage = l.CurrentStage })
            .ToList();

        if (!dryRun)
        {
            await ApplyHistoryCorrectionsAsync(historyUpdates, ct);
            await AddExclusionsAsync(tenantId, unitId, "agendados", toExclude.Select(l => l.Id).ToList(), ct);
        }

        sw.Stop();
        r.DurationMs = sw.ElapsedMilliseconds;
        _logger.LogInformation(
            "[reconcile-ag] {Mode} unit={Unit} csv={Csv} match={Match} corrigidos={Corr} excluidos={Excl}",
            dryRun ? "DRY-RUN" : "APLICADO", unitId, r.CsvRows, r.Matched, r.DatesCorrected, r.ExclusionsAdded);
        return r;
    }

    // ─── Compareceu ──────────────────────────────────────────────────────────

    public async Task<ReconcileResult> ReconcileCompareceuAsync(
        int unitId, int tenantId, Stream csvStream, bool dryRun, CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var r = new ReconcileResult { DryRun = dryRun, KpiKey = "compareceu", UnitId = unitId };

        var (headers, rows) = ParseCsv(csvStream);
        if (headers.Length == 0) return r;

        var iNome = IndexOf(headers, "Nome", "Nome do Cliente");
        var iFone = IndexOf(headers, "Telefone");
        var iDataAg = IndexOf(headers, "Data do Agendamento", "Data");
        if (iNome < 0)
        {
            _logger.LogWarning("[reconcile-comp] CSV sem coluna Nome");
            return r;
        }

        var byPhone = DedupByPhone(rows, iFone, iDataAg, r);
        var matchedLeadIds = new HashSet<int>();

        foreach (var row in byPhone.Values)
        {
            var nome = SafeGet(row, iNome);
            var phone = NormPhone(SafeGet(row, iFone));
            var dataAg = SafeGet(row, iDataAg);

            var words = NameWords(nome);
            var dates = DateVariants(dataAg);
            if (string.IsNullOrEmpty(phone) && (words.Count == 0 || dates.Count == 0))
            { r.Missed++; continue; }

            var match = await FindMatchAsync(unitId, phone, words, dates, ct);
            if (match == null) { r.Missed++; if (r.SampleMissed.Count < 10) r.SampleMissed.Add($"{nome} ({dataAg})"); continue; }
            if (match.Count > 1) { r.Ambiguous++; continue; }

            var lead = match[0];
            if (!matchedLeadIds.Add(lead.Id)) { r.Ambiguous++; continue; }
            r.Matched++;
        }

        r.AttendanceMarked = matchedLeadIds.Count;

        if (!dryRun && matchedLeadIds.Count > 0)
        {
            // Update por lead — segue o padrão do CloudiaCsvImport (ExecuteSqlInterpolated
            // garante parametrização segura sem caçar a sintaxe de array do Npgsql).
            const int BATCH = 500;
            var list = matchedLeadIds.ToList();
            for (var i = 0; i < list.Count; i += BATCH)
            {
                var chunk = list.Skip(i).Take(BATCH).ToList();
                await using var tx = await _db.Database.BeginTransactionAsync(ct);
                try
                {
                    foreach (var id in chunk)
                    {
                        await _db.Database.ExecuteSqlInterpolatedAsync(
                            $@"UPDATE leads
                                  SET ""AttendanceStatus"" = 'compareceu',
                                      ""AttendanceStatusAt"" = NOW() AT TIME ZONE 'UTC'
                                WHERE ""Id"" = {id}", ct);
                    }
                    await tx.CommitAsync(ct);
                }
                catch { await tx.RollbackAsync(ct); throw; }
            }
        }

        sw.Stop();
        r.DurationMs = sw.ElapsedMilliseconds;
        _logger.LogInformation(
            "[reconcile-comp] {Mode} unit={Unit} csv={Csv} match={Match} attendance={Att}",
            dryRun ? "DRY-RUN" : "APLICADO", unitId, r.CsvRows, r.Matched, r.AttendanceMarked);
        return r;
    }

    // ─── Aplicação ───────────────────────────────────────────────────────────

    private async Task ApplyHistoryCorrectionsAsync(
        List<(int historyId, DateTime targetUtc, DateTime fromUtc, string leadName, int leadId)> updates,
        CancellationToken ct)
    {
        const int BATCH = 500;
        for (var i = 0; i < updates.Count; i += BATCH)
        {
            var chunk = updates.Skip(i).Take(BATCH).ToList();
            await using var tx = await _db.Database.BeginTransactionAsync(ct);
            try
            {
                foreach (var u in chunk)
                {
                    await _db.Database.ExecuteSqlInterpolatedAsync(
                        $@"UPDATE lead_stage_histories
                               SET ""CorrectedChangedAt"" = {u.targetUtc},
                                   ""CorrectedAt""        = NOW() AT TIME ZONE 'UTC',
                                   ""CorrectionReason""   = 'reconciliação CSV'
                             WHERE ""Id"" = {u.historyId}", ct);
                }
                await tx.CommitAsync(ct);
            }
            catch
            {
                await tx.RollbackAsync(ct);
                throw;
            }
        }
    }

    private async Task AddExclusionsAsync(
        int tenantId, int unitId, string kpiKey, List<int> leadIds, CancellationToken ct)
    {
        if (leadIds.Count == 0) return;
        var now = DateTime.UtcNow;
        foreach (var leadId in leadIds)
        {
            _db.KpiExclusions.Add(new KpiExclusion
            {
                TenantId = tenantId,
                UnitId = unitId,
                KpiKey = kpiKey,
                LeadId = leadId,
                Reason = "reconciliação CSV: lead não está no relatório",
                ExcludedAt = now,
            });
        }
        await _db.SaveChangesAsync(ct);
    }

    // ─── Helpers (copiados do CloudiaCsvImportService — mesma estratégia) ───

    private sealed record DbMatchRow(int Id, string Name);

    private async Task<List<DbMatchRow>?> FindMatchAsync(
        int unitId, string? phone, List<string> words, List<string> dates, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(phone))
        {
            var variants = new HashSet<string> { phone };
            if (phone.Length >= 11) variants.Add(phone[..2] + phone[3..]);
            if (phone.Length >= 9)  variants.Add(phone[^9..]);
            if (phone.Length >= 8)  variants.Add(phone[^8..]);

            var phonePats = variants.Select(v => $"%{v}%").ToList();
            var phoneRows = await _db.Leads.AsNoTracking()
                .Where(l => l.UnitId == unitId)
                .Where(l => phonePats.Any(p => EF.Functions.ILike(l.Phone, p)))
                .Take(5)
                .Select(l => new DbMatchRow(l.Id, l.Name))
                .ToListAsync(ct);

            if (phoneRows.Count == 1) return phoneRows;
            if (phoneRows.Count > 1)
            {
                var csvWordSet = new HashSet<string>(words);
                var filtered = phoneRows.Where(r => NameWords(r.Name).Any(w => csvWordSet.Contains(w))).ToList();
                if (filtered.Count > 0) return filtered;
            }
        }

        if (words.Count == 0 || dates.Count == 0) return null;
        var q = _db.Leads.AsNoTracking().Where(l => l.UnitId == unitId);
        foreach (var w in words)
        {
            var pat = $"%{w.Replace("%","\\%").Replace("_","\\_")}%";
            q = q.Where(l => EF.Functions.ILike(l.Name, pat));
        }
        var datePats = dates.Select(d => $"%{d}%").ToList();
        q = q.Where(l => datePats.Any(p => EF.Functions.ILike(l.Name, p)));
        var rows = await q.Take(5).Select(l => new DbMatchRow(l.Id, l.Name)).ToListAsync(ct);
        return rows.Count == 0 ? null : rows;
    }

    private static Dictionary<string, string[]> DedupByPhone(
        List<string[]> rows, int iFone, int iDateForTieBreak, ReconcileResult r)
    {
        var byPhone = new Dictionary<string, string[]>();
        foreach (var row in rows)
        {
            r.CsvRows++;
            var phone = NormPhone(SafeGet(row, iFone));
            var key = phone ?? $"__nf_{r.CsvRows}";

            if (byPhone.TryGetValue(key, out var existing))
            {
                if (iDateForTieBreak >= 0)
                {
                    var dtNew = ParseBrDateTime(SafeGet(row, iDateForTieBreak));
                    var dtOld = ParseBrDateTime(SafeGet(existing, iDateForTieBreak));
                    if (dtNew > dtOld) byPhone[key] = row;
                }
            }
            else byPhone[key] = row;
        }
        r.UniqueRows = byPhone.Count;
        return byPhone;
    }

    private static int IndexOf(string[] headers, params string[] candidates)
    {
        for (var i = 0; i < headers.Length; i++)
            foreach (var c in candidates)
                if (headers[i].Trim().Equals(c, StringComparison.OrdinalIgnoreCase)) return i;
        return -1;
    }

    private static string SafeGet(string[] row, int idx)
        => (idx >= 0 && idx < row.Length) ? row[idx] : "";

    private static (string[] Headers, List<string[]> Rows) ParseCsv(Stream csvStream)
    {
        using var reader = new StreamReader(csvStream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var text = reader.ReadToEnd();
        var rows = new List<string[]>();
        var row = new List<string>();
        var cur = new StringBuilder();
        bool inQuotes = false;
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (inQuotes)
            {
                if (ch == '"' && i + 1 < text.Length && text[i + 1] == '"') { cur.Append('"'); i++; }
                else if (ch == '"') inQuotes = false;
                else cur.Append(ch);
            }
            else
            {
                if (ch == '"') inQuotes = true;
                else if (ch == ',') { row.Add(cur.ToString()); cur.Clear(); }
                else if (ch == '\n') { row.Add(cur.ToString()); rows.Add(row.ToArray()); row.Clear(); cur.Clear(); }
                else if (ch == '\r') { /* skip */ }
                else cur.Append(ch);
            }
        }
        if (cur.Length > 0 || row.Count > 0) { row.Add(cur.ToString()); rows.Add(row.ToArray()); }

        if (rows.Count == 0) return (Array.Empty<string>(), new List<string[]>());
        var headers = rows[0];
        var dataRows = rows.Skip(1).Where(r => r.Length == headers.Length).ToList();
        return (headers, dataRows);
    }

    private static string? NormPhone(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var digits = new string(raw.Where(char.IsDigit).ToArray());
        if (digits.Length == 0) return null;
        return digits.Length >= 11 ? digits[^11..] : digits[^Math.Min(10, digits.Length)..];
    }

    private static List<string> NameWords(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return new();
        var norm = s.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var ch in norm)
        {
            var cat = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (cat == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(ch) || ch == ' ') sb.Append(char.ToLower(ch, CultureInfo.InvariantCulture));
            else sb.Append(' ');
        }
        return sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length >= 3 && w.All(char.IsLetter))
            .ToList();
    }

    private static List<string> DateVariants(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return new();
        var m = Regex.Match(s.Trim(), @"^(\d{1,2})/(\d{1,2})/(\d{2,4})");
        if (!m.Success) return new();
        var dd = m.Groups[1].Value; var mo = m.Groups[2].Value; var yy = m.Groups[3].Value;
        if (yy.Length == 4) yy = yy.Substring(2);
        var ddP = dd.PadLeft(2, '0'); var ddN = int.Parse(dd).ToString();
        var mmP = mo.PadLeft(2, '0'); var mmN = int.Parse(mo).ToString();
        return new HashSet<string>
        {
            $"{ddP}/{mmP}/{yy}", $"{ddP}/{mmN}/{yy}",
            $"{ddN}/{mmP}/{yy}", $"{ddN}/{mmN}/{yy}",
        }.ToList();
    }

    private static DateTime ParseBrDateTime(string s)
        => ParseBrDateTimeUtc(s) ?? DateTime.MinValue;

    private static DateTime? ParseBrDateTimeUtc(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var m = Regex.Match(s.Trim(),
            @"^(\d{1,2})/(\d{1,2})/(\d{4})(?:\s+(\d{1,2}):(\d{2})(?::(\d{2}))?)?$");
        if (!m.Success) return null;
        var d  = int.Parse(m.Groups[1].Value);
        var mo = int.Parse(m.Groups[2].Value);
        var y  = int.Parse(m.Groups[3].Value);
        var hh = m.Groups[4].Success ? int.Parse(m.Groups[4].Value) : 12;
        var mi = m.Groups[5].Success ? int.Parse(m.Groups[5].Value) : 0;
        var se = m.Groups[6].Success ? int.Parse(m.Groups[6].Value) : 0;
        try
        {
            var local = new DateTime(y, mo, d, hh, mi, se, DateTimeKind.Unspecified);
            var brTz = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");
            return TimeZoneInfo.ConvertTimeToUtc(local, brTz);
        }
        catch { return null; }
    }
}
