using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;

namespace LeadAnalytics.Api.Service;

public record GeoIpResult(string? Country, string? Region, string? City);

/// <summary>
/// Resolve IP → cidade aproximada usando o serviço grátis ip-api.com (sem chave,
/// limite ~45 req/min). Cacheia por IP em memória. IPs privados/loopback são
/// ignorados. Pensado para ser chamado fora do caminho crítico (fire-and-forget).
/// </summary>
public class GeoIpService
{
    private readonly HttpClient _http;
    private readonly IMemoryCache _cache;
    private readonly ILogger<GeoIpService> _logger;

    public GeoIpService(HttpClient http, IMemoryCache cache, ILogger<GeoIpService> logger)
    {
        _http = http;
        _cache = cache;
        _logger = logger;
    }

    public async Task<GeoIpResult?> LookupAsync(string? ip, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(ip)) return null;
        ip = ip.Trim();

        if (IsPrivateOrLocal(ip)) return null;

        if (_cache.TryGetValue<GeoIpResult>($"geoip:{ip}", out var cached))
            return cached;

        try
        {
            var url = $"http://ip-api.com/json/{Uri.EscapeDataString(ip)}?fields=status,country,regionName,city";
            using var resp = await _http.GetAsync(url, ct);
            if (!resp.IsSuccessStatusCode) return null;

            await using var stream = await resp.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            var root = doc.RootElement;

            if (root.TryGetProperty("status", out var st) && st.GetString() != "success")
                return null;

            var result = new GeoIpResult(
                root.TryGetProperty("country", out var c) ? c.GetString() : null,
                root.TryGetProperty("regionName", out var r) ? r.GetString() : null,
                root.TryGetProperty("city", out var ci) ? ci.GetString() : null);

            _cache.Set($"geoip:{ip}", result, TimeSpan.FromHours(12));
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "GeoIP lookup falhou para {Ip}", ip);
            return null;
        }
    }

    private static bool IsPrivateOrLocal(string ip)
    {
        if (!IPAddress.TryParse(ip, out var addr)) return true;
        if (IPAddress.IsLoopback(addr)) return true;

        var b = addr.GetAddressBytes();
        if (b.Length == 4)
        {
            // 10.0.0.0/8, 172.16.0.0/12, 192.168.0.0/16, 169.254.0.0/16
            if (b[0] == 10) return true;
            if (b[0] == 172 && b[1] >= 16 && b[1] <= 31) return true;
            if (b[0] == 192 && b[1] == 168) return true;
            if (b[0] == 169 && b[1] == 254) return true;
        }
        return false;
    }
}
