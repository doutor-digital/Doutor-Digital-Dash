using LeadAnalytics.Api.DTOs.Response;

namespace LeadAnalytics.Api.Service;

public interface IRelatorioService
{
    Task<byte[]> GerarRelatorioMensalAsync(int clinicId, int mes, int ano, CancellationToken ct);

    Task<RelatorioMensalDadosDto?> ObterResumoMensalAsync(int clinicId, int mes, int ano, CancellationToken ct);
}
