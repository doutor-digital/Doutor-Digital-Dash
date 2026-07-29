namespace LeadAnalytics.Api.DTOs.Spine;

/// <summary>
/// Situação dos tratamentos da unidade, raspada do CRM web da franquia
/// (app.doutorhernia.com.br → /tratamentos/exportar). O módulo "Tratamentos" está
/// bloqueado na API oficial (Bearer 403), então esta é a fonte da situação
/// (EM ANDAMENTO / FINALIZADO / NÃO INICIADO / DESISTÊNCIA) e do valor.
/// </summary>
public class FranquiaTratamentosDto
{
    public int Total { get; set; }
    public decimal ValorTotal { get; set; }

    /// <summary>Quebra por situação do tratamento (coluna "Status" do export).</summary>
    public List<FranquiaTratamentoSituacao> PorSituacao { get; set; } = [];

    /// <summary>Quebra por situação financeira (pago/pendente).</summary>
    public List<FranquiaTratamentoSituacao> PorFinanceiro { get; set; } = [];

    /// <summary>Quando o snapshot foi capturado (UTC).</summary>
    public DateTime AtualizadoEm { get; set; }
}

public class FranquiaTratamentoSituacao
{
    public string Situacao { get; set; } = string.Empty;
    public int Quantidade { get; set; }
    public decimal Valor { get; set; }
}
