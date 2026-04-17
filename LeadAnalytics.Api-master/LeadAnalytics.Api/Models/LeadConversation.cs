namespace LeadAnalytics.Api.Models;

public class LeadConversation
{
    public int Id { get; set; }
    public int LeadId { get; set; }
    public Lead Lead { get; set; } = null!;
    public string Channel { get; set; } = null!; // WhatsApp
    public string? Source { get; set; } // Facebook
    public string ConversationState { get; set; } = "bot";
    public DateTime StartedAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public int? AttendantId { get; set; }
    public Attendant? Attendant { get; set; }
    public ICollection<LeadInteraction> Interactions { get; set; } = [];
}