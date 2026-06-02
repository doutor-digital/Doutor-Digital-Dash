namespace LeadAnalytics.Api.Models;

/// <summary>
/// Uma mensagem dentro de uma <see cref="AgentConversation"/>. Como o agente envia
/// a conversa completa a cada webhook, as mensagens são re-sincronizadas (apagadas e
/// regravadas) no upsert — mantendo idempotência.
/// </summary>
public class AgentMessage
{
    public int Id { get; set; }

    public int AgentConversationId { get; set; }
    public AgentConversation Conversation { get; set; } = null!;

    /// <summary>user | assistant | system | tool.</summary>
    public string Role { get; set; } = "user";

    public string? Content { get; set; }

    /// <summary>Quando a mensagem foi enviada (do payload). Default = ordem recebida.</summary>
    public DateTime SentAt { get; set; }

    /// <summary>Id da mensagem no agente (opcional).</summary>
    public string? ExternalId { get; set; }

    /// <summary>Nome da ferramenta, quando Role = "tool".</summary>
    public string? ToolName { get; set; }

    public string? MetadataJson { get; set; }

    public DateTime CreatedAt { get; set; }
}
