namespace LeadAnalytics.Api.Models;

/// <summary>
/// Tratamento contratado — espelha a planilha "Tratamentos Realizados".
/// Permite até 4 recebimentos (splits do pagamento).
/// </summary>
public class SdrTratamento
{
    public int Id { get; set; }
    public int TenantId { get; set; }

    public int SdrConsultaId { get; set; }
    public SdrConsulta Consulta { get; set; } = null!;

    public int SdrLeadId { get; set; }
    public SdrLead Lead { get; set; } = null!;

    public decimal Valor { get; set; }

    /// <summary>"em_andamento" | "concluido" | "cancelado" | null</summary>
    public string? Status { get; set; }

    /// <summary>"longo_3m" | "medio_2m" | "curto_1m" | null</summary>
    public string? Tipo { get; set; }

    public string? Descricao { get; set; }
    public string? Observacao { get; set; }
    public string? Situacao { get; set; }

    public ICollection<SdrRecebimento> Recebimentos { get; set; } = [];

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
