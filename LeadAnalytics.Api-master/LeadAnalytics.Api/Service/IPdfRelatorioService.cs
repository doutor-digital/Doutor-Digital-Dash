using LeadAnalytics.Api.DTOs.Response;

namespace LeadAnalytics.Api.Service;

public interface IPdfRelatorioService
{
    byte[] Gerar(RelatorioMensalDadosDto dados);
}
