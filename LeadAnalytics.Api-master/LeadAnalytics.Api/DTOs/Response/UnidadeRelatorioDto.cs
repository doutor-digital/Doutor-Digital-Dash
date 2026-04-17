namespace LeadAnalytics.Api.DTOs.Response;

public sealed record UnidadeRelatorioDto(
    int? UnitId,
    string NomeUnidade,
    int QuantidadeLeads
);
