using LeadAnalytics.Api.Models;

namespace LeadAnalytics.Api.Service;

public class LeadEventService(ILogger<LeadEventService> logger)
{
    private readonly ILogger<LeadEventService> _logger = logger;

    public Task ProcessAsync(LeadEvent ev)
    {
        _logger.LogInformation(
            "LeadEvent recebido | Source={Source} ExternalId={ExternalId} Phone={Phone} Stage={Stage} AttendantId={AttendantId}",
            ev.SourceSystem, ev.ExternalId, ev.Phone, ev.Stage, ev.AttendantId);

        return Task.CompletedTask;
    }
}
