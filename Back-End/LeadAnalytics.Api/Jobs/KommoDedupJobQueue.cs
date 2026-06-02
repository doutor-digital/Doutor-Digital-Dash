using System.Threading.Channels;

namespace LeadAnalytics.Api.Jobs;

public record KommoDedupJobRequest(string JobId, int UnitId, string Mode, bool Apply);

public interface IKommoDedupJobQueue
{
    ValueTask EnqueueAsync(KommoDedupJobRequest request, CancellationToken ct = default);
    IAsyncEnumerable<KommoDedupJobRequest> DequeueAllAsync(CancellationToken ct);
}

public class InMemoryKommoDedupJobQueue : IKommoDedupJobQueue
{
    private readonly Channel<KommoDedupJobRequest> _channel =
        Channel.CreateUnbounded<KommoDedupJobRequest>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });

    public ValueTask EnqueueAsync(KommoDedupJobRequest request, CancellationToken ct = default)
        => _channel.Writer.WriteAsync(request, ct);

    public IAsyncEnumerable<KommoDedupJobRequest> DequeueAllAsync(CancellationToken ct)
        => _channel.Reader.ReadAllAsync(ct);
}
