using System.Threading.Channels;

namespace LeadAnalytics.Api.Jobs;

public record LeadDuplicateDeleteJobRequest(
    string JobId,
    int? TenantId,
    bool IgnoreTenant,
    int BatchSize,
    bool TagInKommo);

public interface ILeadDuplicateDeleteJobQueue
{
    ValueTask EnqueueAsync(LeadDuplicateDeleteJobRequest request, CancellationToken ct = default);
    IAsyncEnumerable<LeadDuplicateDeleteJobRequest> DequeueAllAsync(CancellationToken ct);
}

public class InMemoryLeadDuplicateDeleteJobQueue : ILeadDuplicateDeleteJobQueue
{
    private readonly Channel<LeadDuplicateDeleteJobRequest> _channel =
        Channel.CreateUnbounded<LeadDuplicateDeleteJobRequest>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });

    public ValueTask EnqueueAsync(LeadDuplicateDeleteJobRequest request, CancellationToken ct = default)
        => _channel.Writer.WriteAsync(request, ct);

    public IAsyncEnumerable<LeadDuplicateDeleteJobRequest> DequeueAllAsync(CancellationToken ct)
        => _channel.Reader.ReadAllAsync(ct);
}
