using System.Globalization;
using System.Text;
using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.DTOs.Imports;
using Microsoft.EntityFrameworkCore;

namespace LeadAnalytics.Api.Service;

/// <summary>
/// Importa o CSV "Cadastro Geral" (formato Cloudia) e corrige a data REAL dos leads
/// históricos no nosso DB. Match por nome+data no nome (convenção da SDR escreve o
/// nome assim: "Edileusa 01/02/25"). Dedup por telefone (vence a entrada mais recente).
/// UPDATEa Lead.OriginalCreatedAt e Lead.LeadType — não toca em CustomFieldsJson nem
/// na Kommo (isso é responsabilidade do backfill ao vivo).
/// </summary>
public class CloudiaCsvImportService(AppDbContext db, ILogger<CloudiaCsvImportService> logger)
{
    private readonly AppDbContext _db = db;
    private readonly ILogger<CloudiaCsvImportService> _logger = logger;

    public async Task<CloudiaCsvImportResultDto> ProcessAsync(
        int unitId,
        Stream csvStream,
        bool dryRun,
        bool updateLeadType,
        CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = new CloudiaCsvImportResultDto { DryRun = dryRun };

        // 1) Parse CSV
        var (headers, rows) = ParseCsv(csvStream);
        if (headers.Length == 0) return result;

        // Aceita "Imperatriz", "Imperatriz-MA", etc. — só filtra leads cuja "Clínica"
        // não bate com o nome da unidade alvo. Como o CSV mistura unidades, e o user
        // sobe um CSV específico de cada unidade, deixamos isso ao critério dele.
        var iNome  = IndexOf(headers, "Nome do Cliente", "nome");
        var iFone  = IndexOf(headers, "Telefone", "telefone");
        var iData  = IndexOf(headers, "Data");
        var iDataOrig = IndexOf(headers, "Data Origem", "Data de Origem");
        var iTipo  = IndexOf(headers, "Tipo");

        if (iNome < 0 || iDataOrig < 0)
        {
            _logger.LogWarning("CSV sem colunas obrigatórias (Nome do Cliente / Data Origem)");
            return result;
        }

        // 2) Dedup por telefone — vence a entrada com Data Origem mais recente.
        var byPhone = new Dictionary<string, string[]>();
        var dupSamples = new List<string>();
        foreach (var row in rows)
        {
            result.TotalRows++;
            var phone = NormPhone(SafeGet(row, iFone));
            var key = phone ?? $"__nf_{result.TotalRows}";

            if (byPhone.TryGetValue(key, out var existing))
            {
                var dtNew = ParseBrDateTime(SafeGet(row, iDataOrig));
                var dtOld = ParseBrDateTime(SafeGet(existing, iDataOrig));
                if (dtNew > dtOld)
                {
                    if (dupSamples.Count < 5) dupSamples.Add($"{SafeGet(existing, iNome)} ({SafeGet(existing, iDataOrig)})");
                    byPhone[key] = row;
                }
                else
                {
                    if (dupSamples.Count < 5) dupSamples.Add($"{SafeGet(row, iNome)} ({SafeGet(row, iDataOrig)})");
                }
                result.DuplicatesRemoved++;
            }
            else byPhone[key] = row;
        }
        result.UniqueRows = byPhone.Count;
        result.SampleDuplicates = dupSamples;

        // 3) Para cada linha, match por nome+data no DB (UnitId = unitId)
        var writes = new List<(int id, DateTime oca)>();
        var sampleMatches = new List<CloudiaCsvSampleMatchDto>();
        var sampleMissed = new List<string>();
        var distMonth = new Dictionary<string, int>();
        var usedDbIds = new HashSet<int>();

        foreach (var row in byPhone.Values)
        {
            var nome = SafeGet(row, iNome);
            var dataCsv = SafeGet(row, iData);
            var dataOrigem = SafeGet(row, iDataOrig);

            var words = NameWords(nome);
            var dates = DateVariants(dataCsv);
            if (words.Count == 0 || dates.Count == 0)
            {
                result.InvalidInput++;
                continue;
            }

            var oca = ParseBrDateTimeUtc(dataOrigem);
            if (oca == null)
            {
                result.InvalidInput++;
                continue;
            }

            var match = await FindMatchAsync(unitId, words, dates, ct);
            if (match == null)
            {
                result.Missed++;
                if (sampleMissed.Count < 5) sampleMissed.Add($"{nome} ({dataCsv})");
                continue;
            }
            if (match.Count > 1)
            {
                result.Ambiguous++;
                continue;
            }
            var lead = match[0];
            if (!usedDbIds.Add(lead.Id))
            {
                result.Ambiguous++; // mesmo DB lead já reivindicado por outra linha
                continue;
            }

            result.Matched++;
            writes.Add((lead.Id, oca.Value));

            if (sampleMatches.Count < 10)
                sampleMatches.Add(new CloudiaCsvSampleMatchDto
                {
                    CsvName = nome,
                    DbName = lead.Name,
                    DataOrigem = dataOrigem,
                    DbLeadId = lead.Id,
                });

            var monthKey = TimeZoneInfo.ConvertTimeFromUtc(oca.Value,
                TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo")).ToString("yyyy-MM");
            distMonth[monthKey] = distMonth.GetValueOrDefault(monthKey) + 1;
        }

        result.SampleMatches = sampleMatches;
        result.SampleMissed = sampleMissed;
        result.DistributionByMonth = distMonth.OrderBy(kv => kv.Key).ToDictionary(kv => kv.Key, kv => kv.Value);

        // 4) Aplica UPDATEs em batches (skip se dry-run)
        if (!dryRun && writes.Count > 0)
        {
            const int BATCH = 500;
            for (var i = 0; i < writes.Count; i += BATCH)
            {
                var batch = writes.Skip(i).Take(BATCH).ToList();
                await using var tx = await _db.Database.BeginTransactionAsync(ct);
                try
                {
                    foreach (var w in batch)
                    {
                        if (updateLeadType)
                        {
                            await _db.Database.ExecuteSqlInterpolatedAsync(
                                $@"UPDATE leads
                                       SET ""OriginalCreatedAt"" = {w.oca},
                                           ""LeadType"" = 'resgate'
                                     WHERE ""Id"" = {w.id}", ct);
                        }
                        else
                        {
                            await _db.Database.ExecuteSqlInterpolatedAsync(
                                $@"UPDATE leads
                                       SET ""OriginalCreatedAt"" = {w.oca}
                                     WHERE ""Id"" = {w.id}", ct);
                        }
                        result.Updated++;
                    }
                    await tx.CommitAsync(ct);
                }
                catch (Exception ex)
                {
                    await tx.RollbackAsync(ct);
                    _logger.LogError(ex, "Batch UPDATE falhou (batch {Index})", i / BATCH);
                    throw;
                }
            }
        }

        sw.Stop();
        result.DurationMs = sw.ElapsedMilliseconds;
        _logger.LogInformation(
            "📥 Cloudia CSV import {Mode}. Unit={Unit} Total={Total} Unique={Unique} Match={Match} Ambig={Ambig} Miss={Miss} Updated={Updated}",
            dryRun ? "DRY-RUN" : "APLICADO", unitId, result.TotalRows, result.UniqueRows,
            result.Matched, result.Ambiguous, result.Missed, result.Updated);

        return result;
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private sealed record DbMatchRow(int Id, string Name);

    private async Task<List<DbMatchRow>?> FindMatchAsync(int unitId, List<string> words, List<string> dates, CancellationToken ct)
    {
        var q = _db.Leads.AsNoTracking().Where(l => l.UnitId == unitId);

        // Todas as palavras do CSV têm que estar no Name (rigoroso)
        foreach (var w in words)
        {
            var pat = $"%{w.Replace("%","\\%").Replace("_","\\_")}%";
            q = q.Where(l => EF.Functions.ILike(l.Name, pat));
        }

        // Pelo menos 1 das variações da data tem que estar no Name
        var datePats = dates.Select(d => $"%{d}%").ToList();
        q = q.Where(l => datePats.Any(p => EF.Functions.ILike(l.Name, p)));

        var rows = await q.Take(5).Select(l => new DbMatchRow(l.Id, l.Name)).ToListAsync(ct);
        return rows.Count == 0 ? null : rows;
    }

    private static int IndexOf(string[] headers, params string[] candidates)
    {
        for (var i = 0; i < headers.Length; i++)
        {
            foreach (var c in candidates)
                if (headers[i].Trim().Equals(c, StringComparison.OrdinalIgnoreCase)) return i;
        }
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
        var m = System.Text.RegularExpressions.Regex.Match(s.Trim(), @"^(\d{1,2})/(\d{1,2})/(\d{2,4})");
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
    {
        var t = ParseBrDateTimeUtc(s);
        return t ?? DateTime.MinValue;
    }

    private static DateTime? ParseBrDateTimeUtc(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        // Aceita "dd/MM/yyyy HH:mm:ss" e "dd/MM/yyyy"
        var m = System.Text.RegularExpressions.Regex.Match(s.Trim(),
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
