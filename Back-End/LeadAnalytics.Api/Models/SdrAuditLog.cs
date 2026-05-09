namespace LeadAnalytics.Api.Models;

/// <summary>
/// Log de auditoria do fluxo SDR — toda ação importante fica registrada para a chefe poder consultar.
/// Cobre: aprovação de revisão, rejeição, edição manual, criação manual, exclusão, importação.
/// </summary>
public class SdrAuditLog
{
    public int Id { get; set; }
    public int TenantId { get; set; }

    /// <summary>Usuário que executou a ação. Null em ações automáticas (webhook).</summary>
    public int? UserId { get; set; }
    public User? User { get; set; }

    /// <summary>Cache do nome/e-mail no momento da ação (caso o user seja deletado depois).</summary>
    public string? UserName { get; set; }
    public string? UserEmail { get; set; }

    /// <summary>"sdr_lead.review_approved" | "sdr_lead.review_rejected" | "sdr_lead.created_manual"
    /// | "sdr_lead.updated" | "sdr_lead.deleted" | "sdr_lead.imported"
    /// | "sdr_consulta.created" | "sdr_tratamento.created" | etc.</summary>
    public string Action { get; set; } = null!;

    /// <summary>"SdrLead" | "SdrConsulta" | "SdrTratamento" | "SdrTarefa" | "SdrAgendaEvento" | "SdrMeta"</summary>
    public string EntityType { get; set; } = null!;
    public int EntityId { get; set; }

    /// <summary>Resumo legível pra exibir na timeline (ex.: "Aprovou revisão de João da Silva").</summary>
    public string Summary { get; set; } = null!;

    /// <summary>JSON com os campos antes da mudança (null em CREATE).</summary>
    public string? BeforeJson { get; set; }

    /// <summary>JSON com os campos depois da mudança (null em DELETE).</summary>
    public string? AfterJson { get; set; }

    /// <summary>IP do request, opcional.</summary>
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }

    public DateTime CreatedAt { get; set; }
}
