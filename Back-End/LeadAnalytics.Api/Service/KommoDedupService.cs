using System.Text;
using System.Text.RegularExpressions;
using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.DTOs.Admin;
using LeadAnalytics.Api.Jobs;
using Microsoft.EntityFrameworkCore;

namespace LeadAnalytics.Api.Service;

/// <summary>
/// Deduplicação que lê a Kommo AO VIVO (API) e marca os duplicados com a tag
/// "DUPLICADO" na própria Kommo — sem depender do nosso banco. Mantém o lead mais
/// avançado (maior valor → mais antigo) e tagueia os demais. Não apaga (a API da
/// Kommo não permite); o usuário filtra a tag na Kommo e apaga em massa por lá.
/// </summary>
public partial class KommoDedupService(
    AppDbContext db,
    KommoApiClient kommo,
    ILogger<KommoDedupService> logger)
{
    private readonly AppDbContext _db = db;
    private readonly KommoApiClient _kommo = kommo;
    private readonly ILogger<KommoDedupService> _logger = logger;

    private const string DuplicateTag = "DUPLICADO";
    private const string PhoneCode = "PHONE";
    private const int PageSize = 250;
    private const int MaxLeads = 20000;
    private const string ModeName = "name";

    public async Task RunAsync(KommoDedupJobDto job, KommoDedupJobStore store, CancellationToken ct)
    {
        var unit = await _db.Units.AsNoTracking().FirstOrDefaultAsync(u => u.Id == job.UnitId, ct);
        if (unit is null)
            throw new InvalidOperationException("Unidade não encontrada.");
        if (string.IsNullOrWhiteSpace(unit.KommoSubdomain) || string.IsNullOrWhiteSpace(unit.KommoAccessToken))
            throw new InvalidOperationException("Unidade sem KommoSubdomain/KommoAccessToken configurados.");

        var sub = unit.KommoSubdomain!;
        var token = unit.KommoAccessToken!;

        job.Status = DuplicateDeleteJobStatus.Running;
        job.StartedAt = DateTime.UtcNow;
        await store.SaveAsync(job, ct);

        // 1) Busca todos os leads (com contatos) paginando.
        var leads = new List<KommoApiLead>();
        var contactIds = new HashSet<long>();
        var page = 1;
        while (leads.Count < MaxLeads)
        {
            var resp = await _kommo.GetLeadsPageAsync(sub, token, page, PageSize, ct, withContacts: true);
            var batch = resp?.Embedded?.Leads;
            if (batch is null || batch.Count == 0) break;

            foreach (var l in batch)
            {
                if (l.IsDeleted) continue;
                leads.Add(l);
                foreach (var c in l.Embedded?.Contacts ?? [])
                    contactIds.Add(c.Id);
            }

            job.LeadsFetched = leads.Count;
            await store.SaveAsync(job, ct);

            if (resp?.Links?.Next is null) break;
            page++;
        }

        // 2) Busca os contatos (em lotes) pra resolver o telefone.
        var phoneByContact = new Dictionary<long, string>();
        foreach (var chunk in Chunk(contactIds, 250))
        {
            var resp = await _kommo.GetContactsByIdsAsync(sub, token, chunk, ct);
            foreach (var c in resp?.Embedded?.Contacts ?? [])
            {
                var p = FindCustomField(c.CustomFieldsValues, PhoneCode);
                if (!string.IsNullOrWhiteSpace(p)) phoneByContact[c.Id] = p!;
            }
        }

        // 3) Monta o índice (chave de agrupamento por telefone ou nome).
        var isName = string.Equals(job.Mode, ModeName, StringComparison.OrdinalIgnoreCase);
        var items = new List<Item>(leads.Count);
        foreach (var l in leads)
        {
            string key;
            if (isName)
            {
                key = CanonicalName(l.Name);
                if (key.Length < 2) continue;
            }
            else
            {
                var phone = FindCustomField(l.CustomFieldsValues, PhoneCode);
                if (string.IsNullOrWhiteSpace(phone))
                {
                    var cref = l.Embedded?.Contacts?.FirstOrDefault(c => c.IsMain)
                             ?? l.Embedded?.Contacts?.FirstOrDefault();
                    if (cref is not null) phoneByContact.TryGetValue(cref.Id, out phone);
                }
                key = CanonicalPhone(phone ?? string.Empty);
                if (key.Length < 8) continue;
            }

            var tags = (l.Embedded?.Tags ?? [])
                .Select(t => t.Name)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Select(n => n!)
                .ToList();

            items.Add(new Item(l.Id, key, l.Price ?? 0, l.CreatedAt ?? 0, tags));
        }

        // 4) Agrupa; em cada grupo mantém o mais avançado (maior valor → mais antigo).
        var toTag = new List<(long Id, List<string> Tags)>();
        var groups = 0;
        foreach (var g in items.GroupBy(i => i.Key))
        {
            var ordered = g.OrderByDescending(i => i.Price).ThenBy(i => i.CreatedAt).ThenBy(i => i.LeadId).ToList();
            if (ordered.Count < 2) continue;
            groups++;

            foreach (var dup in ordered.Skip(1))
            {
                var tagNames = new List<string>(dup.Tags);
                if (!tagNames.Contains(DuplicateTag, StringComparer.OrdinalIgnoreCase))
                    tagNames.Add(DuplicateTag);
                toTag.Add((dup.LeadId, tagNames));
            }
        }

        job.GroupsFound = groups;
        job.LeadsToTag = toTag.Count;
        await store.SaveAsync(job, ct);

        _logger.LogWarning(
            "🔎 Kommo dedup unit {Unit}: {Leads} leads, {Groups} grupos, {ToTag} a taguear (mode={Mode})",
            unit.Id, leads.Count, groups, toTag.Count, job.Mode);

        // 5) Marca a tag DUPLICADO em lotes de 50, confirmando por re-GET.
        foreach (var chunk in Chunk(toTag, 50))
        {
            ct.ThrowIfCancellationRequested();
            var list = chunk.ToList();
            try
            {
                job.Tagged += await _kommo.PatchLeadsTagsAsync(sub, token, list, ct);
                var ids = list.Select(x => x.Id);
                var confirmedSet = await _kommo.GetLeadIdsWithTagAsync(sub, token, ids, DuplicateTag, ct);
                job.Confirmed += confirmedSet.Count;
            }
            catch (Exception ex)
            {
                job.Failed += list.Count;
                _logger.LogWarning(ex, "Falha ao taguear lote na Kommo (unit {Unit})", unit.Id);
            }
            await store.SaveAsync(job, ct);
        }

        job.Status = DuplicateDeleteJobStatus.Completed;
        job.FinishedAt = DateTime.UtcNow;
        await store.SaveAsync(job, ct);

        _logger.LogWarning(
            "✅ Kommo dedup unit {Unit} concluído: tagueados={Tagged} confirmados={Confirmed} falhas={Failed}",
            unit.Id, job.Tagged, job.Confirmed, job.Failed);
    }

    private readonly record struct Item(long LeadId, string Key, long Price, long CreatedAt, List<string> Tags);

    private static string? FindCustomField(List<KommoApiCustomField>? fields, string code)
    {
        var f = fields?.FirstOrDefault(x => string.Equals(x.FieldCode, code, StringComparison.OrdinalIgnoreCase));
        return f?.Values?.FirstOrDefault()?.GetStringValue();
    }

    private static string CanonicalPhone(string raw)
    {
        var d = OnlyDigits(raw);
        if ((d.Length == 12 || d.Length == 13) && d.StartsWith("55", StringComparison.Ordinal))
            d = d[2..];
        return d;
    }

    private static string CanonicalName(string raw)
        => SpaceRegex().Replace(raw.Trim(), " ").ToLowerInvariant();

    private static string OnlyDigits(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s) if (char.IsDigit(c)) sb.Append(c);
        return sb.ToString();
    }

    private static IEnumerable<List<T>> Chunk<T>(IEnumerable<T> source, int size)
    {
        var bucket = new List<T>(size);
        foreach (var item in source)
        {
            bucket.Add(item);
            if (bucket.Count == size)
            {
                yield return bucket;
                bucket = new List<T>(size);
            }
        }
        if (bucket.Count > 0) yield return bucket;
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex SpaceRegex();
}
