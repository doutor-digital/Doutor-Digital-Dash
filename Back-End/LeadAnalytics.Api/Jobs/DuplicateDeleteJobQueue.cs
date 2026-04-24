using System.Threading.Channels;

namespace LeadAnalytics.Api.Jobs;

public record DuplicateDeleteJobRequest(
    string JobId,
    int? TenantId,
    bool IgnoreTenant,
    int BatchSize);

public interface IDuplicateDeleteJobQueue
{
    ValueTask EnqueueAsync(DuplicateDeleteJobRequest request, CancellationToken ct = default);
    IAsyncEnumerable<DuplicateDeleteJobRequest> DequeueAllAsync(CancellationToken ct);
}

public class InMemoryDuplicateDeleteJobQueue : IDuplicateDeleteJobQueue
{
    private readonly Channel<DuplicateDeleteJobRequest> _channel =
        Channel.CreateUnbounded<DuplicateDeleteJobRequest>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });

    public ValueTask EnqueueAsync(DuplicateDeleteJobRequest request, CancellationToken ct = default)
        => _channel.Writer.WriteAsync(request, ct);

    public IAsyncEnumerable<DuplicateDeleteJobRequest> DequeueAllAsync(CancellationToken ct)
        => _channel.Reader.ReadAllAsync(ct);
}
