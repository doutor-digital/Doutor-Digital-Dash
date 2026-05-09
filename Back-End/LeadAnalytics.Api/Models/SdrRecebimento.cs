namespace LeadAnalytics.Api.Models;

/// <summary>
/// Recebimento (split de pagamento) — usado por SdrConsulta (até 2) e SdrTratamento (até 4).
/// Polimórfico: SdrConsultaId XOR SdrTratamentoId está preenchido (nunca os dois).
/// </summary>
public class SdrRecebimento
{
    public int Id { get; set; }
    public int TenantId { get; set; }

    public int? SdrConsultaId { get; set; }
    public SdrConsulta? Consulta { get; set; }

    public int? SdrTratamentoId { get; set; }
    public SdrTratamento? Tratamento { get; set; }

    /// <summary>Ordem dentro do parent (1, 2, 3, 4...).</summary>
    public int Ordem { get; set; }

    public decimal Valor { get; set; }

    /// <summary>"Pix" | "Dinheiro" | "Débito" | "Boleto" | "Crédito Nx"</summary>
    public string FormaPagamento { get; set; } = null!;

    public DateTime DataRecebimento { get; set; }
    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; }
}
