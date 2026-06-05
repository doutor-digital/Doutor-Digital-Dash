using System.Text.Json.Serialization;

namespace LeadAnalytics.Api.DTOs.Conversations;

/// <summary>
/// Espelha 1:1 o contrato consumido pelo frontend
/// (<c>Front-End/src/services/messages.ts</c>). Os nomes em snake_case são
/// intencionais — o front respeita o nome da coluna como vem do backend.
/// </summary>
public sealed class MessageEventDto
{
    [JsonPropertyName("mensagem_id")]
    public string MensagemId { get; init; } = string.Empty;

    [JsonPropertyName("lead_id")]
    public string LeadId { get; init; } = string.Empty;

    /// <summary><c>entrada</c> | <c>saida</c></summary>
    [JsonPropertyName("direcao")]
    public string Direcao { get; init; } = "entrada";

    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; init; }

    /// <summary>texto | imagem | audio | video | documento</summary>
    [JsonPropertyName("tipo")]
    public string Tipo { get; init; } = "texto";

    [JsonPropertyName("agente")]
    public string? Agente { get; init; }

    [JsonPropertyName("campanha")]
    public string Campanha { get; init; } = string.Empty;
}

public sealed class MessagesListResponse
{
    [JsonPropertyName("items")]
    public IReadOnlyList<MessageEventDto> Items { get; init; } = Array.Empty<MessageEventDto>();

    [JsonPropertyName("nextCursor")]
    public string? NextCursor { get; init; }
}
