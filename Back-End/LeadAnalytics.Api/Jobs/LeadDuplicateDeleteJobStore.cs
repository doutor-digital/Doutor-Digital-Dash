using System.Text.Json;
using LeadAnalytics.Api.DTOs.Admin;
using Microsoft.Extensions.Caching.Distributed;

namespace LeadAnalytics.Api.Jobs;

public class LeadDuplicateDeleteJobStore(IDistributedCache cache)
{
    private readonly IDistributedCache _cache = cache;
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(24);
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private static string Key(string id) => $"lead-dup-delete-job:v1:{id}";

    public async Task SaveAsync(LeadDuplicateDeleteJobDto job, CancellationToken ct = default)
    {
        var payload = JsonSerializer.Serialize(job, JsonOpts);
        await _cache.SetStringAsync(
            Key(job.Id),
            payload,
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = Ttl },
            ct);
    }

    public async Task<LeadDuplicateDeleteJobDto?> GetAsync(string id, CancellationToken ct = default)
    {
        var payload = await _cache.GetStringAsync(Key(id), ct);
        return string.IsNullOrEmpty(payload)
            ? null
            : JsonSerializer.Deserialize<LeadDuplicateDeleteJobDto>(payload, JsonOpts);
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
