namespace LeadAnalytics.Api.Models;

/// <summary>
/// Evento normalizado de uma entidade vinda de um CRM externo (Kommo, Cloudia, …).
/// É a fronteira entre o formato cru do webhook e o domínio interno.
/// </summary>
public class LeadEvent
{
    public string ExternalId { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Stage { get; set; } = string.Empty;
    public string AttendantId { get; set; } = string.Empty;
    public string SourceSystem { get; set; } = string.Empty;

    // ─── Contexto do evento (preenchido pelo Kommo) ───────────────
    /// <summary>add | update | delete | restore | status | responsible | note | accept | decline</summary>
    public string Action { get; set; } = string.Empty;
    /// <summary>lead | contact | company | task | note | unsorted | message | talk | catalog</summary>
    public string EntityType { get; set; } = "lead";

    public string? Name { get; set; }
    public string? Email { get; set; }
    public string? Price { get; set; }
    public string? PipelineId { get; set; }
    public string? OldStage { get; set; }
    public string? OldAttendantId { get; set; }
    public string? AccountId { get; set; }
}
