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
    IHttpContextAccessor httpContextAccessor)
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

         // 🔍 diagnóstico — remover depois                                                                                                       
        _logger.LogInformation(
        "Cloudia token fingerprint: len={Len} head={Head} tail={Tail} hasBearerPrefix={HasBearer} hasWhitespace={HasWs}",                    
        token.Length,                                                                                                                        
        token.Length >= 6 ? token[..6] : token,
        token.Length >= 6 ? token[^6..] : token,                                                                                             
        token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase),
        token.Any(char.IsWhiteSpace));                                                                                                       
                                    

        var baseUrl = ResolveBaseUrl();
        var url = $"{baseUrl}/api/clinics/{clinicId}/dashboard/real-time" +
                  $"?attendantType={attendantType}&metricType=BUSINESS_PERIOD";
        _logger.LogInformation("Cloudia request URL: {Url}", url);   
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");

        try
        {
            var response = await _httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Erro ao buscar métricas da Cloudia: {Status}", response.StatusCode);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<CloudiaMetricsResponseDto>(json, JsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha na chamada à Cloudia ({Url})", url);
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
