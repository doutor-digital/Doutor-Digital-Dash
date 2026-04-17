//using LeadAnalytics.Api.DTOs;
//using Microsoft.EntityFrameworkCore;
//using System.Text.Json;

//namespace LeadAnalytics.Api.Service
//{
//    public class MetricsServiceBase
//    {

//        public async Task<CloudiaMetricsResponseDto?> GetDashboardAsync(
//            int clinicId,
//            string attendantType = "HUMAN")
//        {
//            var token = _config["Cloudia:Token"];
//            var url = $"https://human-metrics.cloudiabot.com/api/clinics/{clinicId}/dashboard/real-time" +
//                      $"?attendantType={attendantType}&metricType=BUSINESS_PERIOD";

//            _httpClient.DefaultRequestHeaders.Clear();
//            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");

//            var response = await _httpClient.GetAsync(url);

//            if (!response.IsSuccessStatusCode)
//            {
//                _logger.LogWarning("Erro ao buscar métricas da Cloudia: {Status}", response.StatusCode);
//                return null;
//            }

//            var json = await response.Content.ReadAsStringAsync();

//            return JsonSerializer.Deserialize<CloudiaMetricsResponseDto>(json, new JsonSerializerOptions
//            {
//                PropertyNameCaseInsensitive = true
//            });
//        }
//    }
//}