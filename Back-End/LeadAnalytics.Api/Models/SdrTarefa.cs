namespace LeadAnalytics.Api.Models;

/// <summary>
/// Tarefa do time SDR — espelha a planilha "Tarefas".
/// Manual (Cloudia não traz tarefas).
/// </summary>
public class SdrTarefa
{
    public int Id { get; set; }
    public int TenantId { get; set; }

    public DateTime DataVencimento { get; set; }
    public string Nome { get; set; } = null!;
    public string? Descricao { get; set; }

    /// <summary>"baixa" | "media" | "alta"</summary>
    public string Prioridade { get; set; } = "media";

    /// <summary>"pendente" | "em_andamento" | "concluida" | "cancelada"</summary>
    public string Status { get; set; } = "pendente";

    public string? Observacao { get; set; }

    /// <summary>e-mail do responsável (login).</summary>
    public string? ResponsavelLogin { get; set; }

    /// <summary>Tarefa ligada a um lead específico (opcional).</summary>
    public int? SdrLeadId { get; set; }
    public SdrLead? Lead { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? ConcludedAt { get; set; }
}
