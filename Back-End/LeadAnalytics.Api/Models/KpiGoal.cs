namespace LeadAnalytics.Api.Models;

/// <summary>
/// Meta mensal de UM KPI em UMA unidade, definida pelo gestor.
///
/// POR QUE NO SERVIDOR
/// -------------------
/// Já existia meta no produto, guardada no localStorage do navegador. Isso significa que
/// cada pessoa via uma meta diferente, que ela sumia ao trocar de computador, e que a
/// gerente não tinha como saber qual número a equipe estava perseguindo. Meta que só uma
/// pessoa enxerga não é meta — é lembrete.
///
/// É por unidade porque a mesma rede tem clínicas de tamanhos diferentes: 200 leads/mês é
/// um mês fraco numa e um recorde em outra.
///
/// O ALVO SOZINHO NÃO BASTA
/// ------------------------
/// "22 de 200" no dia 5 parece fracasso e é adiantado. O que torna a meta útil no meio do
/// mês é o ritmo — quanto já deveria estar feito a esta altura —, e isso o dashboard
/// calcula a partir daqui, sem guardar nada a mais.
/// </summary>
public class KpiGoal
{
    public int Id { get; set; }

    /// <summary>Unidade (clínica) dona da meta.</summary>
    public int UnitId { get; set; }
    public Unit? Unit { get; set; }

    /// <summary>Tenant (Unit.ClinicId) — redundante com a unidade, evita join na leitura.</summary>
    public int ClinicId { get; set; }

    /// <summary>
    /// Chave do KPI no dashboard — a mesma de <see cref="KpiConfiguration.KpiKey"/>
    /// (ex.: "total_leads", "agendados", "consultas", "tratamentos").
    /// </summary>
    public string KpiKey { get; set; } = null!;

    /// <summary>
    /// Alvo do mês inteiro. Decimal porque nem toda meta é contagem: receita e ticket
    /// também entram aqui.
    /// </summary>
    public decimal MetaMensal { get; set; }

    /// <summary>E-mail de quem definiu por último (auditoria leve).</summary>
    public string? UpdatedByEmail { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
