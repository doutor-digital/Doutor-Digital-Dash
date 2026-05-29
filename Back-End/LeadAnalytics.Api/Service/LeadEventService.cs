using LeadAnalytics.Api.Models;

namespace LeadAnalytics.Api.Service;

public class LeadEventService(ILogger<LeadEventService> logger)
{
    private readonly ILogger<LeadEventService> _logger = logger;

    public Task ProcessAsync(LeadEvent ev)
    {
        _logger.LogInformation(
            "LeadEvent | Source={Source} Entity={Entity} Action={Action} ExternalId={ExternalId} Phone={Phone} Stage={Stage} (old={OldStage}) AttendantId={AttendantId} (old={OldAttendantId})",
            ev.SourceSystem, ev.EntityType, ev.Action, ev.ExternalId, ev.Phone, ev.Stage, ev.OldStage, ev.AttendantId, ev.OldAttendantId);

        return Task.CompletedTask;
    }
}
