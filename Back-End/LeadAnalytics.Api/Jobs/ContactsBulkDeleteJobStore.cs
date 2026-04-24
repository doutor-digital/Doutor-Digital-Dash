using System.Text.Json;
using LeadAnalytics.Api.DTOs.Admin;
using Microsoft.Extensions.Caching.Distributed;

namespace LeadAnalytics.Api.Jobs;

public class ContactsBulkDeleteJobStore(IDistributedCache cache)
{
    private readonly IDistributedCache _cache = cache;
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(24);
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private static string Key(string id) => $"contacts-bulk-delete:v1:{id}";
    private static string SelectionKey(string id) => $"contacts-bulk-delete-sel:v1:{id}";

    public async Task SaveAsync(ContactsBulkDeleteJobDto job, CancellationToken ct = default)
    {
        await _cache.SetStringAsync(
            Key(job.Id),
            JsonSerializer.Serialize(job, JsonOpts),
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = Ttl },
            ct);
    }

    public async Task SaveSelectionAsync(string jobId, ContactsBulkDeleteSelection selection, CancellationToken ct = default)
    {
        await _cache.SetStringAsync(
            SelectionKey(jobId),
            JsonSerializer.Serialize(selection, JsonOpts),
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = Ttl },
            ct);
    }

    public async Task<ContactsBulkDeleteJobDto?> GetAsync(string id, CancellationToken ct = default)
    {
        var payload = await _cache.GetStringAsync(Key(id), ct);
        return string.IsNullOrEmpty(payload)
            ? null
            : JsonSerializer.Deserialize<ContactsBulkDeleteJobDto>(payload, JsonOpts);
    }

    public async Task<ContactsBulkDeleteSelection?> GetSelectionAsync(string id, CancellationToken ct = default)
    {
        var payload = await _cache.GetStringAsync(SelectionKey(id), ct);
        return string.IsNullOrEmpty(payload)
            ? null
            : JsonSerializer.Deserialize<ContactsBulkDeleteSelection>(payload, JsonOpts);
    }

    public async Task<bool> RequestCancelAsync(string id, CancellationToken ct = default)
    {
        var job = await GetAsync(id, ct);
        if (job is null) return false;
        if (job.Status is DuplicateDeleteJobStatus.Completed
            or DuplicateDeleteJobStatus.Failed
            or DuplicateDeleteJobStatus.Cancelled) return false;

        job.Status = DuplicateDeleteJobStatus.Cancelling;
        await SaveAsync(job, ct);
        return true;
    }
}
