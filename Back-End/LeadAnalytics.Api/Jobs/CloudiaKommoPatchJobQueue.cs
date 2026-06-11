using System.Threading.Channels;

namespace LeadAnalytics.Api.Jobs;

public record CloudiaKommoPatchJobRequest(string JobId);

public interface ICloudiaKommoPatchJobQueue
{
    ValueTask EnqueueAsync(CloudiaKommoPatchJobRequest request, CancellationToken ct = default);
    IAsyncEnumerable<CloudiaKommoPatchJobRequest> DequeueAllAsync(CancellationToken ct);
}

public class InMemoryCloudiaKommoPatchJobQueue : ICloudiaKommoPatchJobQueue
{
    private readonly Channel<CloudiaKommoPatchJobRequest> _channel =
        Channel.CreateUnbounded<CloudiaKommoPatchJobRequest>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });

    public ValueTask EnqueueAsync(CloudiaKommoPatchJobRequest request, CancellationToken ct = default)
        => _channel.Writer.WriteAsync(request, ct);

    public IAsyncEnumerable<CloudiaKommoPatchJobRequest> DequeueAllAsync(CancellationToken ct)
        => _channel.Reader.ReadAllAsync(ct);
}
