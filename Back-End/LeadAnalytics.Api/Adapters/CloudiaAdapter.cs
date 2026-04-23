using LeadAnalytics.Api.DTOs.Cloudia;
using LeadAnalytics.Api.Models;

namespace LeadAnalytics.Api.Adapters;


public class CloudiaAdapter
{
    public LeadEvent ToLeadEvent(CloudiaWebhookDto payload)
    {
        var data = payload.Data ?? payload.Customer;

        return new LeadEvent
        {
            ExternalId = data?.Id.ToString() ?? string.Empty,
            Phone = data?.Phone ?? string.Empty,
            Stage = data?.Stage ?? string.Empty,
            
            AttendantId = payload.AssignedUserId?.ToString() ?? string.Empty,
            SourceSystem = "Cloudia"
        };
    }
}
