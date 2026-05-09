namespace LeadAnalytics.Api.Models;

/// <summary>
/// Consulta realizada — espelha a planilha "Consultas Realizadas".
/// Permite até 2 recebimentos da consulta (entrada + complemento).
/// Após a consulta pode ou não fechar tratamento.
/// </summary>
public class SdrConsulta
{
    public int Id { get; set; }
    public int TenantId { get; set; }

    public int SdrLeadId { get; set; }
    public SdrLead Lead { get; set; } = null!;

    public DateTime DataConsulta { get; set; }
    public decimal ValorConsulta { get; set; }
    public bool Pago { get; set; }

    /// <summary>"compareceu" | "faltou" | "remarcou" | null</summary>
    public string? Status { get; set; }

    public string? TipoTratamentoIndicado { get; set; }
    public decimal? ValorTratamento { get; set; }

    public bool? FechouTratamento { get; set; }
    public string? MotivoNaoFechamento { get; set; }

    public string? Observacao { get; set; }

    public ICollection<SdrRecebimento> Recebimentos { get; set; } = [];
    public ICollection<SdrTratamento> Tratamentos { get; set; } = [];

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
