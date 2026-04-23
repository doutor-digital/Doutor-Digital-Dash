using LeadAnalytics.Api.DTOs.Kommo;
using LeadAnalytics.Api.Models;

namespace LeadAnalytics.Api.Adapters;

public class KommoAdapter
{
    public LeadEvent ToLeadEvent(KommoWebhookDto payload)
    {
        return new LeadEvent
        {
            ExternalId = payload.Id ?? string.Empty,
            Phone = payload.Phone ?? string.Empty,
            Stage = payload.StatusId ?? string.Empty,
            AttendantId = payload.ResponsibleUserId ?? string.Empty,
            SourceSystem = "Kommo"
        };
    }
}
