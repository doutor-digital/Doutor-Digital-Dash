namespace LeadAnalytics.Api.Models;

public class LeadInteraction
{
    public int Id { get; set; }
    public int LeadConversationId { get; set; }
    public LeadConversation Conversation { get; set; } = null!;
    public string Type { get; set; } = null!;
    // MESSAGE_SENT, MESSAGE_RECEIVED, ASSIGNED, PAYMENT, APPOINTMENT
    public string? Content { get; set; }
    public string? Metadata { get; set; } // JSON webhook

    public DateTime CreatedAt { get; set; }
}
