using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.DTOs.Cloudia;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace LeadAnalytics.Api.Service;

public class MetricsService(
    HttpClient httpClient,
    ILogger<MetricsService> logger,
    IConfiguration config,
    ConfigurationService configurationService,
    IHttpContextAccessor httpContextAccessor,
    CloudiaTokenProvider cloudiaTokenProvider)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient = httpClient;
    private readonly ILogger<MetricsService> _logger = logger;
    private readonly IConfiguration _config = config;
    private readonly ConfigurationService _configurationService = configurationService;
    private readonly IHttpContextAccessor _httpContextAccessor = httpContextAccessor;
    private readonly CloudiaTokenProvider _cloudiaTokenProvider = cloudiaTokenProvider;

    private string? HeaderValue(string name)
    {
        var ctx = _httpContextAccessor.HttpContext;
        if (ctx is null) return null;
        return ctx.Request.Headers.TryGetValue(name, out var value)
            ? value.ToString()
            : null;
    }

    private async Task<string?> ResolveTokenAsync()
    {
        var headerToken = HeaderValue("X-Cloudia-Bearer");
        if (!string.IsNullOrWhiteSpace(headerToken))
            return headerToken;

        // Provider: tenta auto-renovar usando credenciais configuradas
        var providerToken = await _cloudiaTokenProvider.GetTokenAsync();
        if (!string.IsNullOrWhiteSpace(providerToken))
            return providerToken;

        // Fallback: token estático do DB ou appsettings
        var dbToken = await _configurationService.GetCloudiaApiKeyAsync();
        if (!string.IsNullOrWhiteSpace(dbToken)) return dbToken;

        return _config["Cloudia:Token"];
    }

    private string ResolveBaseUrl()
    {
        var headerUrl = HeaderValue("X-Cloudia-Base-Url");
        if (!string.IsNullOrWhiteSpace(headerUrl))
            return headerUrl.TrimEnd('/');

        var configured = _config["Cloudia:BaseUrl"];
        return string.IsNullOrWhiteSpace(configured)
            ? "https://human-metrics.cloudiabot.com"
            : configured.TrimEnd('/');
    }

    public async Task<CloudiaMetricsResponseDto?> GetDashboardAsync(
        int clinicId,
        string attendantType = "HUMAN")
    {
        var token = await ResolveTokenAsync();
        if (string.IsNullOrWhiteSpace(token))
        {
            _logger.LogWarning("Cloudia token não configurado — configure em /settings");
            return null;
        }

        var baseUrl = ResolveBaseUrl();
        var response = await SendRequestAsync(baseUrl, clinicId, attendantType, token);

        // Se 401, invalida cache e tenta de novo
        if (response?.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            _logger.LogWarning("Recebido 401 de Cloudia — renovando token");
            _cloudiaTokenProvider.InvalidateCache();

            var newToken = await ResolveTokenAsync();
            if (!string.IsNullOrWhiteSpace(newToken))
            {
                response = await SendRequestAsync(baseUrl, clinicId, attendantType, newToken);
            }
        }

        if (response?.IsSuccessStatusCode ?? false)
        {
            var json = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<CloudiaMetricsResponseDto>(json, JsonOptions);
        }

        _logger.LogWarning("Erro ao buscar métricas da Cloudia: {Status}",
            response?.StatusCode ?? System.Net.HttpStatusCode.InternalServerError);
        return null;
    }

    private async Task<HttpResponseMessage?> SendRequestAsync(
        string baseUrl,
        int clinicId,
        string attendantType,
        string token)
    {
        try
        {
            var url = $"{baseUrl}/api/clinics/{clinicId}/dashboard/real-time" +
                      $"?attendantType={attendantType}&metricType=BUSINESS_PERIOD";
            _logger.LogInformation("Cloudia request URL: {Url}", url);

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");

            return await _httpClient.SendAsync(request);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha na chamada à Cloudia");
            return null;
        }
    }

    public async Task<object?> GetDashboardComHistoricoAsync(
    int clinicId,
    AppDbContext db)
    {
        var metrics = await GetDashboardAsync(clinicId, "HUMAN");
        if (metrics is null) return null;

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

        var atendentes = metrics.AttendantsServicesList
            .Where(a => a.AttendantId is not null)
            .Select(a => new
            {
                Nome = a.AttendantName,
                EmAtendimentoAgora = a.TotalServices,
                AguardandoResposta = a.TotalWaitingForResponse,
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
