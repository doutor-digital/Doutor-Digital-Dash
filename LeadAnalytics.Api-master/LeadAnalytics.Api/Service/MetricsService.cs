using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.DTOs.Cloudia;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace LeadAnalytics.Api.Service;

public class MetricsService(
    HttpClient httpClient,
    ILogger<MetricsService> logger,
    IConfiguration config)
{
    private readonly HttpClient _httpClient = httpClient;
    private readonly ILogger<MetricsService> _logger = logger;
    private readonly IConfiguration _config = config;

    public async Task<CloudiaMetricsResponseDto?> GetDashboardAsync(
        int clinicId,
        string attendantType = "HUMAN")
    {
        var token = _config["Cloudia:Token"];
        var url = $"https://human-metrics.cloudiabotom/api/clinics/{clinicId}/dashboard/real-time" +
                  $"?attendantType={attendantType}&metricType=BUSINESS_PERIOD";

        _httpClient.DefaultRequestHeaders.Clear();
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");

        var response = await _httpClient.GetAsync(url);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Erro ao buscar métricas da Cloudia: {Status}", response.StatusCode);
            return null;
        }

        var json = await response.Content.ReadAsStringAsync();

        return JsonSerializer.Deserialize<CloudiaMetricsResponseDto>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
    }

    public async Task<object?> GetDashboardComHistoricoAsync(
    int clinicId,
    AppDbContext db)
    {
        // 1. Busca métricas em tempo real
        var metrics = await GetDashboardAsync(clinicId, "HUMAN");
        if (metrics is null) return null;

        // 2. Busca conversões históricas por atendente
        var conversoes = await db.LeadAssignments
            .Include(a => a.Attendant)
            .Include(a => a.Lead)
            .Where(a => a.Lead.TenantId == clinicId)
            .GroupBy(a => new { a.Attendant.ExternalId, a.Attendant.Name })
            .Select(g => new
            {
                AttendantId = g.Key.ExternalId,
                Nome = g.Key.Name,
                Convertidos = g.Count(a =>
                    a.Lead.CurrentStage == "09_FECHOU_TRATAMENTO" ||
                    a.Lead.CurrentStage == "10_EM_TRATAMENTO" ||
                    a.Lead.CurrentStage == "05_AGENDADO_COM_PAGAMENTO")
            })
            .ToListAsync();

        // 3. Cruza os dois
        var atendentes = metrics.AttendantsServicesList
            .Where(a => a.AttendantId is not null)
            .Select(a => new
            {
                Nome = a.AttendantName,
                EmAtendimentoAgora = a.TotalServices,
                AguardandoResposta = a.TotalWaitingForResponse,
                // Busca conversões históricas pelo ExternalId
                ConversoesFechadas = conversoes
                    .FirstOrDefault(c => c.AttendantId == a.AttendantId)
                    ?.Convertidos ?? 0
            })
            .OrderByDescending(x => x.ConversoesFechadas);

        return new
        {
            Resumo = new
            {
                TotalEmAtendimento = metrics.Metrics.TotalInService,
                TotalNaFila = metrics.Metrics.TotalInQueue,
                TempoMedioResposta = Math.Round(metrics.Metrics.WaitResponseTimeAvg, 1)
            },
            Atendentes = atendentes
        };
    }
}