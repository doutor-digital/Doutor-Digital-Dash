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

    // ─── Snapshot de dados da Kommo (JSON cru) ───────────────
    /// <summary>JSON com os custom fields do lead (array de {field_id, field_name, field_code, type, value}).</summary>
    public string? CustomFieldsJson { get; set; }

    /// <summary>JSON com as tags do lead (array de strings).</summary>
    public string? TagsJson { get; set; }

    /// <summary>
    /// Data de criação REAL do lead na Kommo (UTC). Quando preenchida, sobrescreve
    /// o <c>Lead.CreatedAt</c> do nosso banco (que originalmente era gravado como
    /// "data do primeiro sync" e bagunçava a contagem de leads do dia).
    /// </summary>
    public DateTime? KommoCreatedAtUtc { get; set; }
}
