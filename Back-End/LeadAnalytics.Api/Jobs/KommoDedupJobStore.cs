using System.Text.Json;
using LeadAnalytics.Api.DTOs.Admin;
using Microsoft.Extensions.Caching.Distributed;

namespace LeadAnalytics.Api.Jobs;

public class KommoDedupJobStore(IDistributedCache cache)
{
    private readonly IDistributedCache _cache = cache;
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(24);
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private static string Key(string id) => $"kommo-dedup-job:v1:{id}";

    public async Task SaveAsync(KommoDedupJobDto job, CancellationToken ct = default)
    {
        await _cache.SetStringAsync(
            Key(job.Id),
            JsonSerializer.Serialize(job, JsonOpts),
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = Ttl },
            ct);
    }

    public async Task<KommoDedupJobDto?> GetAsync(string id, CancellationToken ct = default)
    {
        var payload = await _cache.GetStringAsync(Key(id), ct);
        return string.IsNullOrEmpty(payload)
            ? null
            : JsonSerializer.Deserialize<KommoDedupJobDto>(payload, JsonOpts);
    }
}
