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
    /// Loop de chat com function-calling. Recebe a lista de tools que o modelo
    /// pode invocar. A cada tool_call no response, chama <paramref name="executor"/>
    /// e re-injeta o resultado como mensagem `tool` na próxima chamada — até o
    /// modelo retornar `content` em vez de tool_calls (ou estourar o cap de loops).
    ///
    /// <para>Stream de eventos para o front via callback (cada tool invocada, cada token
    /// final, etc.) é o que permite a UI mostrar "buscando KPIs…" enquanto roda.</para>
    /// </summary>
    public async Task<string> ChatWithToolsAsync(
        string apiKey,
        string systemPrompt,
        IEnumerable<(string Role, string Content)> history,
        IReadOnlyList<(string Name, string Description, JsonElement Schema)> tools,
        Func<string, JsonElement, CancellationToken, Task<string>> executor,
        CancellationToken ct,
        Action<string, string>? onToolCall = null,
        string model = DefaultModel,
        double temperature = 0.4,
        int maxTokens = 1500,
        int maxLoops = 6)
    {
        // Constrói lista inicial de mensagens
        var msgs = new List<object> { new { role = "system", content = systemPrompt } };
        foreach (var (role, content) in history)
        {
            if (string.IsNullOrWhiteSpace(content)) continue;
            var r = role == "assistant" ? "assistant" : "user";
            msgs.Add(new { role = r, content });
        }

        // Empacota tools no formato esperado pela OpenAI
        var toolsPayload = tools.Select(t => new
        {
            type = "function",
            function = new { name = t.Name, description = t.Description, parameters = t.Schema },
        }).ToArray();

        for (var loop = 0; loop < maxLoops; loop++)
        {
            var payload = new
            {
                model,
                temperature,
                max_tokens = maxTokens,
                messages = msgs,
                tools = toolsPayload,
                tool_choice = "auto",
            };

            using var req = new HttpRequestMessage(HttpMethod.Post, ChatUrl);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            using var resp = await http.SendAsync(req, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);

            if (!resp.IsSuccessStatusCode)
            {
                logger.LogWarning("OpenAI tools {Status}: {Body}", (int)resp.StatusCode, Truncate(body, 500));
                throw new HttpRequestException($"OpenAI retornou {(int)resp.StatusCode}: {Truncate(body, 200)}");
            }

            using var doc = JsonDocument.Parse(body);
            var message = doc.RootElement.GetProperty("choices")[0].GetProperty("message");

            // Sem tool_calls → resposta final
            if (!message.TryGetProperty("tool_calls", out var toolCalls) || toolCalls.ValueKind != JsonValueKind.Array || toolCalls.GetArrayLength() == 0)
            {
                return message.TryGetProperty("content", out var c) ? c.GetString() ?? "" : "";
            }

            // Empilha a mensagem do assistente (com tool_calls) e os resultados
            msgs.Add(JsonSerializer.Deserialize<object>(message.GetRawText())!);

            foreach (var call in toolCalls.EnumerateArray())
            {
                var callId = call.GetProperty("id").GetString() ?? "";
                var fn = call.GetProperty("function");
                var name = fn.GetProperty("name").GetString() ?? "";
                var argsRaw = fn.GetProperty("arguments").GetString() ?? "{}";

                onToolCall?.Invoke(name, argsRaw);

                JsonElement argsElement;
                try
                {
                    using var argsDoc = JsonDocument.Parse(argsRaw);
                    argsElement = argsDoc.RootElement.Clone();
                }
                catch
                {
                    argsElement = JsonDocument.Parse("{}").RootElement;
                }

                string result;
                try
                {
                    result = await executor(name, argsElement, ct);
                }
                catch (Exception ex)
                {
                    result = JsonSerializer.Serialize(new { error = ex.Message });
                }

                msgs.Add(new { role = "tool", tool_call_id = callId, content = result });
            }
        }

        return "Atingi o limite de chamadas internas antes de chegar a uma resposta. Tente reformular a pergunta.";
    }

    /// <summary>
    /// Variante multi-turn de <see cref="ChatAsync"/>: aceita lista completa de
    /// mensagens (user/assistant intercalados). Usada pelo chat flutuante.
    /// </summary>
    public async Task<string> ChatMultiAsync(
        string apiKey,
        string systemPrompt,
        IEnumerable<(string Role, string Content)> history,
        CancellationToken ct,
        string model = DefaultModel,
        double temperature = 0.5,
        int maxTokens = 1200)
    {
        var msgs = new List<object> { new { role = "system", content = systemPrompt } };
        foreach (var (role, content) in history)
        {
            if (string.IsNullOrWhiteSpace(content)) continue;
            var r = role == "assistant" ? "assistant" : "user";
            msgs.Add(new { role = r, content });
        }

        var payload = new { model, temperature, max_tokens = maxTokens, messages = msgs };

        using var req = new HttpRequestMessage(HttpMethod.Post, ChatUrl);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        using var resp = await http.SendAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
        {
            logger.LogWarning("OpenAI chat {Status}: {Body}", (int)resp.StatusCode, Truncate(body, 500));
            throw new HttpRequestException($"OpenAI retornou {(int)resp.StatusCode}: {Truncate(body, 200)}");
        }

        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";
    }

    /// <summary>
    /// Transcreve áudio (mp3/wav/m4a/webm) via gpt-4o-mini-transcribe (substituto
    /// moderno do whisper-1 anunciado pela OpenAI em mar/2025). Mesmo endpoint,
    /// mais barato e melhor em pt-BR. Usado pelas SDRs que querem ditar em vez
    /// de digitar.
    /// </summary>
    public async Task<string> TranscribeAsync(
        string apiKey,
        Stream audioStream,
        string fileName,
        CancellationToken ct,
        string model = "gpt-4o-mini-transcribe")
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
