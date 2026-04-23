using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.DTOs.Timeline;
using Microsoft.EntityFrameworkCore;

namespace LeadAnalytics.Api.Service;

public class LeadTimelineService(AppDbContext db)
{
    private readonly AppDbContext _db = db;

    public async Task<LeadTimelineDto?> GetTimelineAsync(int leadId, CancellationToken ct = default)
    {
        var lead = await _db.Leads
            .AsNoTracking()
            .Include(l => l.StageHistory)
            .Include(l => l.Conversations).ThenInclude(c => c.Attendant)
            .Include(l => l.Conversations).ThenInclude(c => c.Interactions)
            .Include(l => l.Assignments).ThenInclude(a => a.Attendant)
            .FirstOrDefaultAsync(l => l.Id == leadId, ct);

        if (lead is null) return null;

        var attribution = await _db.LeadAttributions
            .AsNoTracking()
            .Where(a => a.LeadId == leadId)
            .OrderByDescending(a => a.MatchedAt)
            .FirstOrDefaultAsync(ct);

        var now = DateTime.UtcNow;
        var endReference = lead.ConvertedAt ?? now;

        // ── Stages: ordenados por entrada, duração = próxima entrada (ou endReference para o último) ──
        var stagesOrdered = lead.StageHistory.OrderBy(s => s.ChangedAt).ToList();
        var stages = new List<StageStepDto>(stagesOrdered.Count);
        for (int i = 0; i < stagesOrdered.Count; i++)
        {
            var current = stagesOrdered[i];
            var exited = i + 1 < stagesOrdered.Count ? stagesOrdered[i + 1].ChangedAt : (DateTime?)null;
            var effectiveExit = exited ?? endReference;
            stages.Add(new StageStepDto
            {
                Label = current.StageLabel,
                StageId = current.StageId,
                EnteredAt = current.ChangedAt,
                ExitedAt = exited,
                DurationMinutes = Math.Round((effectiveExit - current.ChangedAt).TotalMinutes, 2),
                IsCurrent = exited is null
            });
        }

        // ── Assignments: tempo até primeira interação RECEIVED após cada atribuição ──
        var assignmentsOrdered = lead.Assignments.OrderBy(a => a.AssignedAt).ToList();
        var allInteractions = lead.Conversations
            .SelectMany(c => c.Interactions)
            .OrderBy(i => i.CreatedAt)
            .ToList();

        var assignments = assignmentsOrdered.Select(a => new AssignmentStepDto
        {
            AttendantId = a.AttendantId,
            AttendantName = a.Attendant?.Name ?? "(desconhecido)",
            StageAtAssignment = a.Stage,
            AssignedAt = a.AssignedAt,
            MinutesUntilFirstReply = MinutesUntilFirstReply(a.AssignedAt, allInteractions)
        }).ToList();

        var conversations = lead.Conversations
            .OrderBy(c => c.StartedAt)
            .Select(c => new ConversationDto
            {
                Id = c.Id,
                Channel = c.Channel,
                Source = c.Source,
                ConversationState = c.ConversationState,
                AttendantName = c.Attendant?.Name,
                StartedAt = c.StartedAt,
                EndedAt = c.EndedAt,
                DurationMinutes = Math.Round(((c.EndedAt ?? endReference) - c.StartedAt).TotalMinutes, 2),
                InteractionsCount = c.Interactions.Count
            }).ToList();

        var interactions = allInteractions.Select(i => new InteractionDto
        {
            Id = i.Id,
            ConversationId = i.LeadConversationId,
            Type = i.Type,
            Content = i.Content,
            CreatedAt = i.CreatedAt
        }).ToList();

        // ── Insights ──
        var firstAssignment = assignmentsOrdered.FirstOrDefault();
        var minutesPerState = MinutesPerConversationState(lead.Conversations, endReference);
        var longestStage = stages.OrderByDescending(s => s.DurationMinutes ?? 0).FirstOrDefault();

        var insights = new TimelineInsightsDto
        {
            TotalMinutesUntilConversion = lead.ConvertedAt is null
                ? null
                : Math.Round((lead.ConvertedAt.Value - lead.CreatedAt).TotalMinutes, 2),
            MinutesUntilFirstAssignment = firstAssignment is null
                ? null
                : Math.Round((firstAssignment.AssignedAt - lead.CreatedAt).TotalMinutes, 2),
            MinutesInBot = minutesPerState.GetValueOrDefault("bot", 0),
            MinutesInQueue = minutesPerState.GetValueOrDefault("queue", 0),
            MinutesInService = minutesPerState.GetValueOrDefault("service", 0),
            StageChanges = Math.Max(0, stages.Count - 1),
            Reassignments = Math.Max(0, assignmentsOrdered.Count - 1),
            LongestStageLabel = longestStage?.Label,
            LongestStageMinutes = longestStage?.DurationMinutes
        };

        return new LeadTimelineDto
        {
            Lead = new LeadHeaderDto
            {
                Id = lead.Id,
                ExternalId = lead.ExternalId,
                Name = lead.Name,
                Phone = lead.Phone,
                Source = lead.Source,
                Channel = lead.Channel,
                CurrentStage = lead.CurrentStage,
                ConversationState = lead.ConversationState,
                HasAppointment = lead.HasAppointment,
                HasPayment = lead.HasPayment,
                CreatedAt = lead.CreatedAt,
                ConvertedAt = lead.ConvertedAt
            },
            Attribution = attribution is null ? null : new AttributionDto
            {
                Phone = attribution.Phone,
                CtwaClid = attribution.CtwaClid,
                SourceId = attribution.SourceId,
                SourceType = attribution.SourceType,
                MatchType = attribution.MatchType,
                Confidence = attribution.Confidence,
                MatchedAt = attribution.MatchedAt
            },
            Stages = stages,
            Assignments = assignments,
            Conversations = conversations,
            Interactions = interactions,
            Insights = insights
        };
    }

    private static double? MinutesUntilFirstReply(
        DateTime assignedAt,
        IReadOnlyList<Models.LeadInteraction> interactions)
    {
        var first = interactions
            .FirstOrDefault(i => i.CreatedAt > assignedAt &&
                                 (i.Type == "MESSAGE_RECEIVED" || i.Type == "MESSAGE_SENT"));
        return first is null ? null : Math.Round((first.CreatedAt - assignedAt).TotalMinutes, 2);
    }

    private static Dictionary<string, double> MinutesPerConversationState(
        IEnumerable<Models.LeadConversation> conversations,
        DateTime endReference)
    {
        var totals = new Dictionary<string, double>();
        foreach (var c in conversations)
        {
            var minutes = ((c.EndedAt ?? endReference) - c.StartedAt).TotalMinutes;
            if (minutes < 0) continue;
            var key = c.ConversationState ?? "unknown";
            totals[key] = totals.GetValueOrDefault(key, 0) + minutes;
        }
        foreach (var k in totals.Keys.ToList())
            totals[k] = Math.Round(totals[k], 2);
        return totals;
    }
}
