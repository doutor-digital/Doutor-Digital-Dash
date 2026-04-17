namespace LeadAnalytics.Api.DTOs;

/// <summary>
/// DTO para receber dados customizados do n8n
/// Use este formato se o n8n já processar/simplificar o payload da Meta
/// </summary>
public class N8nWebhookDto
{
    public string Phone { get; set; } = null!;
    public string? ContactName { get; set; }

    public string? CtwaClid { get; set; }
    public string? SourceId { get; set; }
    public string? SourceType { get; set; }
    public string? SourceUrl { get; set; }

    public string? Headline { get; set; }
    public string? Body { get; set; }

    public string? MessageId { get; set; }
    public string? MessageTimestamp { get; set; }

    public int? TenantId { get; set; }
}