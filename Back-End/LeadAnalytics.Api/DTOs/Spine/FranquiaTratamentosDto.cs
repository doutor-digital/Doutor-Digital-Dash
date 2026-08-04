namespace LeadAnalytics.Api.DTOs.Spine;

/// <summary>
/// Situação dos tratamentos da unidade (EM ANDAMENTO / FINALIZADO / NÃO INICIADO /
/// DESISTÊNCIA) e o valor.
///
/// A fonte preferida é a rota oficial <c>POST /api/treatments/search</c>, liberada em
/// agosto/2026 — antes disso respondia 403 e a única saída era raspar o export do CRM
/// web. A raspagem continua como reserva para unidade sem token ou quando a API falha;
/// <see cref="Fonte"/> diz de onde o número veio, porque as duas fontes não são
/// equivalentes (veja <see cref="PorFinanceiro"/>).
/// </summary>
public class FranquiaTratamentosDto
{
    public int Total { get; set; }
    public decimal ValorTotal { get; set; }

    /// <summary>Quebra por situação do tratamento (coluna "Status" do export).</summary>
    public List<FranquiaTratamentoSituacao> PorSituacao { get; set; } = [];

    /// <summary>
    /// Quebra por situação financeira (pago/pendente). Só existe no export raspado —
    /// a rota oficial não devolve esse dado, então vem vazia quando <see cref="Fonte"/>
    /// é <c>api</c>.
    /// </summary>
    public List<FranquiaTratamentoSituacao> PorFinanceiro { get; set; } = [];

    /// <summary><c>api</c> (rota oficial) ou <c>web</c> (export raspado).</summary>
    public string Fonte { get; set; } = "web";

    /// <summary>Quando o snapshot foi capturado (UTC).</summary>
    public DateTime AtualizadoEm { get; set; }
}

public class FranquiaTratamentoSituacao
{
    public string Situacao { get; set; } = string.Empty;
    public int Quantidade { get; set; }
    public decimal Valor { get; set; }
}
