namespace LeadAnalytics.Api.DTOs.Response;

public sealed record LeadRelatorioItemDto(
    string Nome,
    string? Telefone,
    string Origem,
    string Stage,
    DateTime CriadoEm
);
