using System.Text.Json.Serialization;

namespace LeadAnalytics.Api.DTOs.Response;

/// <summary>
/// DTO com dados mínimos de leads ativos para sincronização
/// </summary>
public class ActiveLeadDto
{
    /// <summary>
    /// ID interno do lead
    /// </summary>
    [JsonPropertyName("id")]
    public int Id { get; set; }

    /// <summary>
    /// ID externo (Cloudia)
    /// </summary>
    [JsonPropertyName("externalId")]
    public int ExternalId { get; set; }

    /// <summary>
    /// Nome do lead
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = null!;

    /// <summary>
    /// Telefone do lead
    /// </summary>
    [JsonPropertyName("phone")]
    public string Phone { get; set; } = null!;

    /// <summary>
    /// Estado atual da conversa (bot, queue, service, concluido)
    /// </summary>
    [JsonPropertyName("conversationState")]
    public string ConversationState { get; set; } = null!;

    /// <summary>
    /// ID do atendente responsável (se houver)
    /// </summary>
    [JsonPropertyName("attendantId")]
    public int? AttendantId { get; set; }

    /// <summary>
    /// ID da unidade
    /// </summary>
    [JsonPropertyName("unitId")]
    public int? UnitId { get; set; }

    /// <summary>
    /// Data da última atualização
    /// </summary>
    [JsonPropertyName("updatedAt")]
    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// Data de criação
    /// </summary>
    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// DTO para contagem de leads por estado
/// </summary>
public class LeadsCountDto
{
    [JsonPropertyName("bot")]
    public int Bot { get; set; }

    [JsonPropertyName("queue")]
    public int Queue { get; set; }

    [JsonPropertyName("service")]
    public int Service { get; set; }

    [JsonPropertyName("concluido")]
    public int Concluido { get; set; }

    [JsonPropertyName("total")]
    public int Total { get; set; }
}