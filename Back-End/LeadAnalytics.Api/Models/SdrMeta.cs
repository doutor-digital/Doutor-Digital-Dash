namespace LeadAnalytics.Api.Models;

/// <summary>
/// Meta mensal por secretária/login — espelha a planilha "Metas das Secretárias".
/// Único por (TenantId, Mes, Login).
/// </summary>
public class SdrMeta
{
    public int Id { get; set; }
    public int TenantId { get; set; }

    /// <summary>Formato "YYYY-MM".</summary>
    public string Mes { get; set; } = null!;

    public string Unidade { get; set; } = null!;
    public string Login { get; set; } = null!;
    public string Secretaria { get; set; } = null!;

    public decimal MetaValor { get; set; }
    public int RealCadastro { get; set; }
    public int RealResgate { get; set; }
    public int QtdTotal { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
