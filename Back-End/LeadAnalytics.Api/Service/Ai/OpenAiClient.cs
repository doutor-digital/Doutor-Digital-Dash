using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace LeadAnalytics.Api.Service.Ai;

/// <summary>
/// Cliente HTTP enxuto pra OpenAI Chat Completions (gpt-4o-mini por padrão).
/// Sem SDK — uma única chamada POST /v1/chat/completions, response simples.
/// Reaproveita o HttpClient injetado pelo DI (HttpClientFactory).
/// </summary>
public class OpenAiClient(HttpClient http, ILogger<OpenAiClient> logger)
{
    public const string DefaultModel = "gpt-4o-mini";
    private const string ChatUrl = "https://api.openai.com/v1/chat/completions";
    private const string TranscribeUrl = "https://api.openai.com/v1/audio/transcriptions";

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    /// <summary>
    /// Manda system + user messages, devolve só o texto da primeira choice.
    /// Lança HttpRequestException em erro (caller trata e loga).
    /// </summary>
    public async Task<string> ChatAsync(
        string apiKey,
        string systemPrompt,
        string userPrompt,
        CancellationToken ct,
        string model = DefaultModel,
        double temperature = 0.3,
        int maxTokens = 1500)
    {
        var payload = new
        {
            model,
            temperature,
            max_tokens = maxTokens,
            messages = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt },
            },
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, ChatUrl);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        using var resp = await http.SendAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
        {
            logger.LogWarning("OpenAI {Status}: {Body}", (int)resp.StatusCode, Truncate(body, 500));
            throw new HttpRequestException($"OpenAI retornou {(int)resp.StatusCode}: {Truncate(body, 200)}");
        }

        using var doc = JsonDocument.Parse(body);
        var content = doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString();
        return content ?? string.Empty;
    }

    /// <summary>
    /// Transcreve áudio (mp3/wav/m4a/webm) via Whisper. Usado pelas SDRs que querem
    /// ditar em vez de digitar. Modelo padrão: whisper-1.
    /// </summary>
    public async Task<string> TranscribeAsync(
        string apiKey,
        Stream audioStream,
        string fileName,
        CancellationToken ct,
        string model = "whisper-1")
    {
        using var content = new MultipartFormDataContent();
        var streamContent = new StreamContent(audioStream);
        streamContent.Headers.ContentType = new MediaTypeHeaderValue(GuessMime(fileName));
        content.Add(streamContent, "file", fileName);
        content.Add(new StringContent(model), "model");
        content.Add(new StringContent("pt"), "language");

        using var req = new HttpRequestMessage(HttpMethod.Post, TranscribeUrl) { Content = content };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        using var resp = await http.SendAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
        {
            logger.LogWarning("Whisper {Status}: {Body}", (int)resp.StatusCode, Truncate(body, 500));
            throw new HttpRequestException($"Whisper retornou {(int)resp.StatusCode}: {Truncate(body, 200)}");
        }

        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";
    }

    /// <summary>Confere se a key existe e funciona (chama /v1/models, cheap).</summary>
    public async Task<bool> PingAsync(string apiKey, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "https://api.openai.com/v1/models");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        using var resp = await http.SendAsync(req, ct);
        return resp.IsSuccessStatusCode;
    }

    private static string GuessMime(string fileName) => fileName.Split('.').Last().ToLowerInvariant() switch
    {
        "mp3" => "audio/mpeg",
        "wav" => "audio/wav",
        "m4a" => "audio/mp4",
        "webm" => "audio/webm",
        "ogg" => "audio/ogg",
        _ => "audio/mpeg",
    };

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";
}
