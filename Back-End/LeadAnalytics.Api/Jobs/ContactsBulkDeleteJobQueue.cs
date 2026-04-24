using System.Threading.Channels;

namespace LeadAnalytics.Api.Jobs;

public record ContactsBulkDeleteJobRequest(string JobId, int TenantId, int BatchSize);

public interface IContactsBulkDeleteJobQueue
{
    ValueTask EnqueueAsync(ContactsBulkDeleteJobRequest request, CancellationToken ct = default);
    IAsyncEnumerable<ContactsBulkDeleteJobRequest> DequeueAllAsync(CancellationToken ct);
}

public class InMemoryContactsBulkDeleteJobQueue : IContactsBulkDeleteJobQueue
{
    private readonly Channel<ContactsBulkDeleteJobRequest> _channel =
        Channel.CreateUnbounded<ContactsBulkDeleteJobRequest>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });

    public ValueTask EnqueueAsync(ContactsBulkDeleteJobRequest request, CancellationToken ct = default)
        => _channel.Writer.WriteAsync(request, ct);

    public IAsyncEnumerable<ContactsBulkDeleteJobRequest> DequeueAllAsync(CancellationToken ct)
        => _channel.Reader.ReadAllAsync(ct);
}
