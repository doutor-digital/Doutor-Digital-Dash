namespace LeadAnalytics.Api.DTOs.Response;

/// <summary>
/// Agrega todos os dados necessários para a geração do PDF do relatório mensal.
/// Preenchido pelo RelatorioService e consumido pelo IPdfRelatorioService.
/// </summary>
public sealed class RelatorioMensalDadosDto
{
    public required string NomeClinica { get; init; }
    public required int Mes { get; init; }
    public required int Ano { get; init; }

    /// <summary>Data/hora de geração já convertida para o timezone local (America/Sao_Paulo).</summary>
    public required DateTime GeradoEm { get; init; }

    // ── KPIs ──────────────────────────────────────────────────────────────
    public required int TotalLeads { get; init; }

    /// <summary>Percentual de leads com HasAppointment = true, de 0 a 100.</summary>
    public required decimal TaxaConversaoPercent { get; init; }

    /// <summary>Média de Payment.Amount entre leads que possuem ao menos um pagamento.</summary>
    public required decimal TicketMedio { get; init; }

    // ── Agrupamentos ──────────────────────────────────────────────────────
    public required IReadOnlyList<OrigemAgrupadaDto> LeadsPorOrigem { get; init; }
    public required IReadOnlyList<UnidadeRelatorioDto> LeadsPorUnidade { get; init; }
    public required IReadOnlyList<EtapaAgrupadaDto> LeadsPorEtapa { get; init; }
    public required IReadOnlyList<LeadsPorDiaDto> LeadsPorDia { get; init; }

    // ── Listagem detalhada ────────────────────────────────────────────────
    public required IReadOnlyList<LeadRelatorioItemDto> Leads { get; init; }
}
