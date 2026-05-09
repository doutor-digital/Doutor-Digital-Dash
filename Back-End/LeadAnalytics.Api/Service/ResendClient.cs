using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace LeadAnalytics.Api.Service;

/// <summary>
/// Cliente HTTP para a API do Resend (https://resend.com/docs/api-reference/emails/send-email).
/// Lê <c>RESEND_API_KEY</c> e <c>RESEND_FROM</c> do ambiente (com fallback no
/// appsettings em <c>Resend:ApiKey</c> / <c>Resend:From</c>).
///
/// Para emails NÃO caírem em spam, é fundamental verificar o domínio em
/// resend.com/domains (DKIM + SPF + Return-Path). Sem isso, use o domínio
/// padrão `onboarding@resend.dev` que tem entregabilidade ok mas é claramente
/// transacional.
/// </summary>
public class ResendClient
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _config;
    private readonly ILogger<ResendClient> _logger;

    public ResendClient(HttpClient httpClient, IConfiguration config, ILogger<ResendClient> logger)
    {
        _httpClient = httpClient;
        _config = config;
        _logger = logger;

        if (_httpClient.BaseAddress == null)
            _httpClient.BaseAddress = new Uri("https://api.resend.com/");
    }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(GetApiKey());

    public async Task<bool> SendAsync(
        string toEmail,
        string subject,
        string html,
        string? text = null,
        string? replyTo = null,
        CancellationToken ct = default)
    {
        var apiKey = GetApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning("Resend não configurado (RESEND_API_KEY ausente)");
            return false;
        }

        var from = GetFrom();

        using var req = new HttpRequestMessage(HttpMethod.Post, "emails")
        {
            Content = JsonContent.Create(new ResendEmailRequest
            {
                From = from,
                To = new[] { toEmail },
                Subject = subject,
                Html = html,
                Text = text ?? StripHtml(html),
                ReplyTo = replyTo ?? GetReplyTo(),
            })
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        try
        {
            using var resp = await _httpClient.SendAsync(req, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);

            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogError(
                    "Falha Resend: {Status} {Body} (to={To})",
                    resp.StatusCode, body, toEmail);
                return false;
            }

            _logger.LogInformation(
                "📨 Resend enviou para {To}: {Subject}", toEmail, subject);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro de rede ao chamar Resend");
            return false;
        }
    }

    private string? GetApiKey() =>
        Environment.GetEnvironmentVariable("RESEND_API_KEY")
        ?? _config["Resend:ApiKey"];

    private string GetFrom() =>
        Environment.GetEnvironmentVariable("RESEND_FROM")
        ?? _config["Resend:From"]
        ?? "Doutor Digital <noreply@send.doutordigitalconsultoria.com>";

    private string? GetReplyTo() =>
        Environment.GetEnvironmentVariable("RESEND_REPLY_TO")
        ?? _config["Resend:ReplyTo"];

    private static string StripHtml(string html)
    {
        var sb = new System.Text.StringBuilder(html.Length);
        var inTag = false;
        foreach (var c in html)
        {
            if (c == '<') inTag = true;
            else if (c == '>') inTag = false;
            else if (!inTag) sb.Append(c);
        }
        return System.Net.WebUtility.HtmlDecode(sb.ToString())
            .Replace("\r\n", "\n")
            .Trim();
    }

    private class ResendEmailRequest
    {
        [JsonPropertyName("from")]      public string From { get; set; } = string.Empty;
        [JsonPropertyName("to")]        public string[] To { get; set; } = Array.Empty<string>();
        [JsonPropertyName("subject")]   public string Subject { get; set; } = string.Empty;
        [JsonPropertyName("html")]      public string Html { get; set; } = string.Empty;
        [JsonPropertyName("text")]      public string Text { get; set; } = string.Empty;
        [JsonPropertyName("reply_to")]  public string? ReplyTo { get; set; }
    }
}
