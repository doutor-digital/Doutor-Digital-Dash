using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using LeadAnalytics.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace LeadAnalytics.Api.Service.Ai;

/// <summary>
/// Lê a conversa que a Sofia (agente-Dt) teve com o lead e pede pro GPT uma
/// leitura de quem entende de SDR: onde o atendimento ganhou o agendamento,
/// onde perdeu, e o que fazer diferente na próxima.
///
/// POR QUE A ANÁLISE CITA O NÚMERO DA MENSAGEM, E NÃO UM TRECHO
/// ------------------------------------------------------------
/// Trecho copiado pelo modelo é texto gerado: pode sair parafraseado ou
/// inventado, e quem lê não tem como saber. Aqui o modelo devolve o índice da
/// mensagem, nós conferimos se o índice existe e o front mostra a mensagem real
/// do banco. Afirmação sem mensagem válida é descartada antes de chegar na tela.
/// </summary>
public class AnaliseConversaService(
    AppDbContext db,
    OpenAiClient openAi,
    AiKeyStorage keys,
    IMemoryCache cache,
    ILogger<AnaliseConversaService> logger)
{
    /// <summary>Teto de mensagens no prompt. Conversa longa entra pelas pontas.</summary>
    private const int MaxMensagens = 120;

    private const string SystemPrompt =
        "Você é especialista em pré-venda (SDR) de clínica médica e está auditando UMA conversa de " +
        "WhatsApp entre a atendente virtual e um paciente em potencial. O objetivo da atendente é " +
        "levar o paciente a marcar uma consulta de avaliação.\n\n" +
        "Avalie o atendimento como um supervisor experiente avaliaria: o que fez o paciente avançar, " +
        "o que o fez travar, e o que a atendente deveria ter dito. Seja específico e direto, em pt-BR.\n\n" +
        "REGRAS DURAS:\n" +
        "- Toda afirmação sua sobre a conversa precisa apontar o número da mensagem (campo `msg`) que " +
        "a sustenta. Se não houver mensagem que sustente, não afirme.\n" +
        "- Nunca invente fala, nome, preço, horário ou dado que não esteja na conversa.\n" +
        "- Se a conversa for curta demais para julgar, diga isso na `leitura` e devolva as listas vazias.\n" +
        "- Não elogie por elogiar. Se o atendimento foi ruim, diga que foi ruim.\n\n" +
        "Responda SOMENTE com JSON válido neste formato:\n" +
        "{\n" +
        "  \"nota\": 0-10,\n" +
        "  \"leitura\": \"2-3 frases: o que aconteceu nessa conversa e onde ela foi decidida\",\n" +
        "  \"desfecho\": \"agendou\" | \"nao_agendou\" | \"em_aberto\",\n" +
        "  \"viradaMsg\": número da mensagem que virou o jogo (pra bem ou pra mal), ou null,\n" +
        "  \"viradaPorque\": \"por que essa mensagem foi o ponto de virada\",\n" +
        "  \"acertos\": [{ \"msg\": n, \"oQue\": \"o que a atendente fez bem aqui\" }],\n" +
        "  \"falhas\": [{ \"msg\": n, \"oQue\": \"o que saiu errado\", \"emVezDisso\": \"o que dizer no lugar\" }],\n" +
        "  \"checklist\": [{ \"item\": \"Qualificou (queixa, tempo, cidade)\", \"feito\": true|false, \"msg\": n ou null }]\n" +
        "}\n\n" +
        "No checklist use exatamente estes cinco itens, nesta ordem: " +
        "\"Qualificou o paciente\", \"Gerou interesse na avaliação\", \"Ofereceu horário concreto\", " +
        "\"Contornou a objeção\", \"Confirmou o agendamento\".";

    // ─── Leitura da conversa ────────────────────────────────────────────────

    public async Task<ConversaDoLeadDto?> GetConversaAsync(int leadId, int tenantId, CancellationToken ct)
    {
        var conv = await db.AgentConversations.AsNoTracking()
            .Where(c => c.TenantId == tenantId && c.LeadId == leadId)
            .OrderByDescending(c => c.LastMessageAt ?? c.StartedAt)
            .Select(c => new
            {
                c.Id, c.ExternalId, c.AgentName, c.Channel, c.Status, c.HandedOff,
                c.ContactName, c.StartedAt, c.LastMessageAt, c.MessageCount, c.Summary,
            })
            .FirstOrDefaultAsync(ct);

        if (conv is null) return null;

        var msgs = await db.AgentMessages.AsNoTracking()
            .Where(m => m.AgentConversationId == conv.Id)
            .OrderBy(m => m.SentAt).ThenBy(m => m.Id)
            .Select(m => new { m.Role, m.Content, m.SentAt })
            .ToListAsync(ct);

        return new ConversaDoLeadDto
        {
            ConversaId = conv.Id,
            ExternalId = conv.ExternalId,
            Agente = conv.AgentName,
            Canal = conv.Channel,
            Status = conv.Status,
            PassouPraHumano = conv.HandedOff,
            Contato = conv.ContactName,
            Inicio = conv.StartedAt,
            UltimaMensagem = conv.LastMessageAt,
            Resumo = conv.Summary,
            Mensagens = msgs.Select((m, i) => new MensagemDaConversaDto
            {
                Numero = i + 1,
                DeQuem = m.Role == "assistant" ? "atendente" : "paciente",
                Texto = m.Content ?? string.Empty,
                Em = m.SentAt,
            }).ToList(),
        };
    }

    // ─── Análise ────────────────────────────────────────────────────────────

    public async Task<AnaliseConversaDto> AnalisarAsync(
        int leadId, int tenantId, bool forcar, CancellationToken ct)
    {
        var conversa = await GetConversaAsync(leadId, tenantId, ct)
            ?? throw new InvalidOperationException("Este lead não tem conversa registrada com a I.A.");

        if (conversa.Mensagens.Count < 2)
            throw new InvalidOperationException("A conversa tem mensagens demais de menos para ser analisada.");

        // A conversa cresce; a análise velha vira mentira. A chave inclui a hora
        // da última mensagem, então mensagem nova invalida a análise sozinha.
        var chave = $"analise-conversa:{conversa.ConversaId}:{conversa.UltimaMensagem:O}:{conversa.Mensagens.Count}";
        if (!forcar && cache.TryGetValue<AnaliseConversaDto>(chave, out var pronta) && pronta is not null)
            return pronta with { DoCache = true };

        var apiKey = await keys.GetAsync(tenantId, ct);
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("Chave da OpenAI não configurada para esta clínica.");

        var bruto = await openAi.ChatAsync(
            apiKey, SystemPrompt, MontarTranscricao(conversa), ct,
            temperature: 0.2, maxTokens: 1800);

        var analise = Interpretar(bruto, conversa);
        cache.Set(chave, analise, TimeSpan.FromHours(6));

        logger.LogInformation(
            "🧠 Conversa analisada | lead={Lead} conv={Conv} msgs={Msgs} nota={Nota}",
            leadId, conversa.ConversaId, conversa.Mensagens.Count, analise.Nota);

        return analise;
    }

    /// <summary>
    /// Transcrição numerada. O número é o que a análise cita, então ele precisa
    /// ser o mesmo que o front mostra — por isso vem de <c>Numero</c>, não da ordem
    /// do que sobrou depois do corte.
    /// </summary>
    private static string MontarTranscricao(ConversaDoLeadDto c)
    {
        var msgs = c.Mensagens;

        // Conversa muito longa: mantém o começo (qualificação) e o fim (desfecho),
        // que é onde a decisão mora. O miolo repetitivo é o que se perde.
        if (msgs.Count > MaxMensagens)
        {
            var cabeca = msgs.Take(MaxMensagens / 2).ToList();
            var cauda = msgs.Skip(msgs.Count - MaxMensagens / 2).ToList();
            msgs = [.. cabeca, .. cauda];
        }

        var sb = new StringBuilder();
        sb.AppendLine($"Conversa por {c.Canal ?? "whatsapp"} com {c.Contato ?? "o paciente"}.");
        sb.AppendLine($"Início: {c.Inicio:dd/MM/yyyy HH:mm}. Última mensagem: {c.UltimaMensagem:dd/MM/yyyy HH:mm}.");
        if (c.PassouPraHumano) sb.AppendLine("A conversa foi passada para um atendente humano em algum ponto.");
        if (msgs.Count < c.Mensagens.Count)
            sb.AppendLine($"(Conversa longa: das {c.Mensagens.Count} mensagens, o miolo foi omitido.)");
        sb.AppendLine();

        var anterior = default(DateTime?);
        foreach (var m in msgs)
        {
            // O silêncio entre mensagens é parte do atendimento: uma resposta que
            // demorou seis horas explica um lead que esfriou.
            if (anterior is DateTime ant)
            {
                var gap = (m.Em - ant).TotalMinutes;
                if (gap >= 60) sb.AppendLine($"[... {Math.Round(gap / 60)}h sem mensagem ...]");
            }
            sb.AppendLine($"[{m.Numero}] {m.DeQuem}: {m.Texto}");
            anterior = m.Em;
        }
        return sb.ToString();
    }

    /// <summary>
    /// Lê o JSON do modelo e joga fora o que não se sustenta: índice de mensagem
    /// que não existe vira null, item sem texto some.
    /// </summary>
    private static AnaliseConversaDto Interpretar(string bruto, ConversaDoLeadDto conversa)
    {
        var json = ExtrairJson(bruto);
        Cru? cru = null;
        try
        {
            cru = JsonSerializer.Deserialize<Cru>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            });
        }
        catch (JsonException)
        {
            // Cai no fallback abaixo: melhor devolver a leitura crua do que erro.
        }

        var maior = conversa.Mensagens.Count;
        int? Valida(int? n) => n is > 0 && n <= maior ? n : null;

        if (cru is null)
        {
            return new AnaliseConversaDto
            {
                Nota = null,
                Leitura = bruto.Trim(),
                Desfecho = "em_aberto",
                Acertos = [], Falhas = [], Checklist = [],
                AnalisadaEm = DateTime.UtcNow,
                MensagensAnalisadas = conversa.Mensagens.Count,
            };
        }

        return new AnaliseConversaDto
        {
            Nota = cru.Nota is >= 0 and <= 10 ? cru.Nota : null,
            Leitura = (cru.Leitura ?? string.Empty).Trim(),
            Desfecho = cru.Desfecho switch
            {
                "agendou" or "nao_agendou" or "em_aberto" => cru.Desfecho,
                _ => "em_aberto",
            },
            ViradaMsg = Valida(cru.ViradaMsg),
            ViradaPorque = string.IsNullOrWhiteSpace(cru.ViradaPorque) ? null : cru.ViradaPorque.Trim(),
            Acertos = (cru.Acertos ?? [])
                .Where(a => !string.IsNullOrWhiteSpace(a.OQue))
                .Select(a => new PontoDaAnaliseDto { Msg = Valida(a.Msg), OQue = a.OQue!.Trim() })
                .ToList(),
            Falhas = (cru.Falhas ?? [])
                .Where(f => !string.IsNullOrWhiteSpace(f.OQue))
                .Select(f => new PontoDaAnaliseDto
                {
                    Msg = Valida(f.Msg),
                    OQue = f.OQue!.Trim(),
                    EmVezDisso = string.IsNullOrWhiteSpace(f.EmVezDisso) ? null : f.EmVezDisso.Trim(),
                })
                .ToList(),
            Checklist = (cru.Checklist ?? [])
                .Where(i => !string.IsNullOrWhiteSpace(i.Item))
                .Select(i => new ItemChecklistDto
                {
                    Item = i.Item!.Trim(),
                    Feito = i.Feito ?? false,
                    Msg = Valida(i.Msg),
                })
                .ToList(),
            AnalisadaEm = DateTime.UtcNow,
            MensagensAnalisadas = conversa.Mensagens.Count,
        };
    }

    /// <summary>Tira cerca de ```json quando o modelo insiste em mandar bloco de código.</summary>
    private static string ExtrairJson(string s)
    {
        var t = s.Trim();
        var ini = t.IndexOf('{');
        var fim = t.LastIndexOf('}');
        return ini >= 0 && fim > ini ? t[ini..(fim + 1)] : t;
    }

    // Espelho cru do JSON do modelo — nada aqui sai da classe sem passar por Interpretar.
    private sealed record Cru(
        [property: JsonPropertyName("nota")] double? Nota,
        [property: JsonPropertyName("leitura")] string? Leitura,
        [property: JsonPropertyName("desfecho")] string? Desfecho,
        [property: JsonPropertyName("viradaMsg")] int? ViradaMsg,
        [property: JsonPropertyName("viradaPorque")] string? ViradaPorque,
        [property: JsonPropertyName("acertos")] List<CruPonto>? Acertos,
        [property: JsonPropertyName("falhas")] List<CruPonto>? Falhas,
        [property: JsonPropertyName("checklist")] List<CruItem>? Checklist);

    private sealed record CruPonto(
        [property: JsonPropertyName("msg")] int? Msg,
        [property: JsonPropertyName("oQue")] string? OQue,
        [property: JsonPropertyName("emVezDisso")] string? EmVezDisso);

    private sealed record CruItem(
        [property: JsonPropertyName("item")] string? Item,
        [property: JsonPropertyName("feito")] bool? Feito,
        [property: JsonPropertyName("msg")] int? Msg);
}

// ─── DTOs de saída ──────────────────────────────────────────────────────────

public class ConversaDoLeadDto
{
    public int ConversaId { get; set; }
    public string? ExternalId { get; set; }
    public string? Agente { get; set; }
    public string? Canal { get; set; }
    public string? Status { get; set; }
    public bool PassouPraHumano { get; set; }
    public string? Contato { get; set; }
    public DateTime Inicio { get; set; }
    public DateTime? UltimaMensagem { get; set; }
    public string? Resumo { get; set; }
    public List<MensagemDaConversaDto> Mensagens { get; set; } = [];
}

public class MensagemDaConversaDto
{
    /// <summary>Posição na conversa, a partir de 1. É o que a análise cita.</summary>
    public int Numero { get; set; }
    /// <summary>"paciente" ou "atendente".</summary>
    public string DeQuem { get; set; } = "paciente";
    public string Texto { get; set; } = string.Empty;
    public DateTime Em { get; set; }
}

public record AnaliseConversaDto
{
    public double? Nota { get; init; }
    public string Leitura { get; init; } = string.Empty;
    public string Desfecho { get; init; } = "em_aberto";
    public int? ViradaMsg { get; init; }
    public string? ViradaPorque { get; init; }
    public List<PontoDaAnaliseDto> Acertos { get; init; } = [];
    public List<PontoDaAnaliseDto> Falhas { get; init; } = [];
    public List<ItemChecklistDto> Checklist { get; init; } = [];
    public DateTime AnalisadaEm { get; init; }
    public int MensagensAnalisadas { get; init; }
    /// <summary>Veio da análise guardada, sem gastar chamada nova.</summary>
    public bool DoCache { get; init; }
}

public class PontoDaAnaliseDto
{
    public int? Msg { get; set; }
    public string OQue { get; set; } = string.Empty;
    public string? EmVezDisso { get; set; }
}

public class ItemChecklistDto
{
    public string Item { get; set; } = string.Empty;
    public bool Feito { get; set; }
    public int? Msg { get; set; }
}
