namespace LeadAnalytics.Api.Models;

/// <summary>
/// Evento da agenda — espelha a planilha "Agenda / Eventos".
/// </summary>
public class SdrAgendaEvento
{
    public int Id { get; set; }
    public int TenantId { get; set; }

    public DateTime Data { get; set; }

    /// <summary>Formato "HH:MM".</summary>
    public string HoraInicio { get; set; } = null!;

    /// <summary>Formato "HH:MM".</summary>
    public string HoraFim { get; set; } = null!;

    public string Nome { get; set; } = null!;
    public string Descricao { get; set; } = null!;

    /// <summary>"agendado" | "confirmado" | "cancelado" | "realizado"</summary>
    public string Status { get; set; } = "agendado";

    public string? Observacao { get; set; }
    public string? ResponsavelLogin { get; set; }

    public int? SdrLeadId { get; set; }
    public SdrLead? Lead { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
