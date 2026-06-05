using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.DTOs.Conversations;
using LeadAnalytics.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace LeadAnalytics.Api.Service;

/// <summary>
/// Puxa conversas/mensagens diretamente da Kommo (REST v4 — Talks + Notes) e
/// devolve no shape de <see cref="MessageEventDto"/> consumido pelo dashboard
/// <c>/conversas</c>. Complementa <c>agent_messages</c> (que cobre só conversas
/// que passaram pelo agente-Dt) capturando também atendimentos feitos por
/// humanos direto na Kommo.
///
/// <para><b>Estratégia</b></para>
/// <list type="bullet">
/// <item><b>Talks:</b> 1 conversa = 1 par de eventos sintéticos
/// (entrada em <c>created_at</c>, saída em <c>updated_at</c> se houve responsável).</item>
/// <item><b>Notes:</b> tipos <c>service_message</c>/<c>extended_service_message</c>
/// viram eventos individuais com o <c>params.text</c> como mensagem real e
/// <c>params.type</c> determinando direção.</item>
/// </list>
///
/// <para><b>Limitações honestas</b></para>
/// <list type="bullet">
/// <item>Não traz mídia/áudio (a Kommo não expõe via REST — só via Chats API/Amojo).</item>
/// <item>Cap de 1 página (250) por endpoint para não estourar rate-limit (7 RPS Kommo).</item>
/// <item>Cache em memória de 60s por chave (unitId, from, to) — alivia hits repetidos da UI.</item>
/// </list>
/// </summary>
public class KommoConversationsImporter
{
    private const int PageLimit = 250;
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan UsersCacheTtl = TimeSpan.FromHours(1);

    private readonly KommoApiClient _api;
    private readonly AppDbContext _db;
    private readonly IMemoryCache _cache;
    private readonly ILogger<KommoConversationsImporter> _logger;

    public KommoConversationsImporter(
        KommoApiClient api,
        AppDbContext db,
        IMemoryCache cache,
        ILogger<KommoConversationsImporter> logger)
    {
        _api = api;
        _db = db;
        _cache = cache;
        _logger = logger;
    }

    public async Task<IReadOnlyList<MessageEventDto>> ImportAsync(
        Unit unit, DateTime? from, DateTime? to, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(unit.KommoSubdomain) ||
            string.IsNullOrWhiteSpace(unit.KommoAccessToken))
        {
            return Array.Empty<MessageEventDto>();
        }

        var fromUnix = from.HasValue ? new DateTimeOffset(DateTime.SpecifyKind(from.Value, DateTimeKind.Utc)).ToUnixTimeSeconds() : (long?)null;
        var toUnix = to.HasValue ? new DateTimeOffset(DateTime.SpecifyKind(to.Value, DateTimeKind.Utc)).ToUnixTimeSeconds() : (long?)null;

        var cacheKey = $"kommo-conv:{unit.Id}:{fromUnix}:{toUnix}";
        if (_cache.TryGetValue(cacheKey, out IReadOnlyList<MessageEventDto>? cached) && cached is not null)
            return cached;

        var userNames = await GetUserNamesAsync(unit, ct);

        // Mapa leadId(Kommo) → Campaign — carrega antecipadamente pra anotar eventos.
        // Lead.ExternalId é o id do lead na Kommo (int).
        var leadCampaignMap = await _db.Leads
            .AsNoTracking()
            .Where(l => l.TenantId == unit.ClinicId && l.UnitId == unit.Id)
            .Select(l => new { l.ExternalId, l.Campaign })
            .ToListAsync(ct);

        var campaignByExternalId = leadCampaignMap
            .Where(x => x.ExternalId > 0)
            .GroupBy(x => x.ExternalId.ToString())
            .ToDictionary(g => g.Key, g => g.First().Campaign ?? "—");

        var events = new List<MessageEventDto>(512);

        // ─── 1) Talks (cabeçalho de conversa) ──────────────────────────────
        try
        {
            var talks = await _api.GetTalksAsync(
                unit.KommoSubdomain!, unit.KommoAccessToken!,
                fromUnix, toUnix, 1, PageLimit, ct);

            foreach (var talk in talks?.Embedded?.Talks ?? new())
            {
                var leadId = talk.EntityId.ToString();
                var campanha = campaignByExternalId.GetValueOrDefault(leadId, "—");

                if (talk.CreatedAt is long createdAtUnix)
                {
                    events.Add(new MessageEventDto
                    {
                        MensagemId = $"kommo-talk-{talk.TalkId}-start",
                        LeadId = leadId,
                        Direcao = "entrada",
                        Timestamp = DateTimeOffset.FromUnixTimeSeconds(createdAtUnix).UtcDateTime,
                        Tipo = "texto",
                        Agente = null,
                        Campanha = campanha,
                    });
                }

                // Se há responsável e updated > created, considera que houve resposta
                if (talk.ResponsibleUserId is long rid && rid > 0
                    && talk.UpdatedAt is long updatedAtUnix
                    && talk.CreatedAt is long c0 && updatedAtUnix > c0)
                {
                    events.Add(new MessageEventDto
                    {
                        MensagemId = $"kommo-talk-{talk.TalkId}-reply",
                        LeadId = leadId,
                        Direcao = "saida",
                        Timestamp = DateTimeOffset.FromUnixTimeSeconds(updatedAtUnix).UtcDateTime,
                        Tipo = "texto",
                        Agente = ResolveAgente(userNames, rid),
                        Campanha = campanha,
                    });
                }
            }

            _logger.LogInformation(
                "[kommo-importer] unit={Unit} talks={Count}",
                unit.Id, talks?.Embedded?.Talks?.Count ?? 0);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[kommo-importer] falha em Talks (unit={Unit}) — segue sem", unit.Id);
        }

        // ─── 2) Notes de chat (mensagem-a-mensagem quando disponível) ──────
        try
        {
            var notes = await _api.GetLeadNotesAsync(
                unit.KommoSubdomain!, unit.KommoAccessToken!,
                fromUnix, toUnix, noteTypes: null, // usa default chat note types
                1, PageLimit, ct);

            foreach (var note in notes?.Embedded?.Notes ?? new())
            {
                if (note.CreatedAt is not long createdAt) continue;
                var leadId = note.EntityId.ToString();

                // Direção: params.type "in"/"out" quando presente; senão deduz por created_by
                // (created_by = 0 → sistema/inbound).
                var direcao = note.Params?.Type switch
                {
                    "in" or "incoming" => "entrada",
                    "out" or "outgoing" => "saida",
                    _ => (note.CreatedBy is null or 0) ? "entrada" : "saida",
                };

                events.Add(new MessageEventDto
                {
                    MensagemId = $"kommo-note-{note.Id}",
                    LeadId = leadId,
                    Direcao = direcao,
                    Timestamp = DateTimeOffset.FromUnixTimeSeconds(createdAt).UtcDateTime,
                    Tipo = note.NoteType == "extended_service_message" ? "documento" : "texto",
                    Agente = direcao == "saida" && note.CreatedBy is long cb && cb > 0
                        ? ResolveAgente(userNames, cb)
                        : null,
                    Campanha = campaignByExternalId.GetValueOrDefault(leadId, "—"),
                });
            }

            _logger.LogInformation(
                "[kommo-importer] unit={Unit} notes={Count}",
                unit.Id, notes?.Embedded?.Notes?.Count ?? 0);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[kommo-importer] falha em Notes (unit={Unit}) — segue sem", unit.Id);
        }

        _cache.Set(cacheKey, (IReadOnlyList<MessageEventDto>)events, CacheTtl);
        return events;
    }

    /// <summary>
    /// Mapa <c>userId → name</c> da Kommo da unidade. Cacheado por 1h —
    /// usuários mudam raramente e ler a cada request seria desperdício de RPS.
    /// </summary>
    private async Task<Dictionary<long, string>> GetUserNamesAsync(Unit unit, CancellationToken ct)
    {
        var key = $"kommo-users:{unit.Id}";
        if (_cache.TryGetValue(key, out Dictionary<long, string>? cached) && cached is not null)
            return cached;

        var map = new Dictionary<long, string>();
        try
        {
            var resp = await _api.GetUsersAsync(
                unit.KommoSubdomain!, unit.KommoAccessToken!,
                page: 1, limit: PageLimit, ct);

            foreach (var u in resp?.Embedded?.Users ?? new())
            {
                if (!string.IsNullOrWhiteSpace(u.Name))
                    map[u.Id] = u.Name!;
            }

            _logger.LogInformation(
                "[kommo-importer] unit={Unit} users carregados={Count}",
                unit.Id, map.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[kommo-importer] falha ao listar users (unit={Unit}) — usando fallback por ID", unit.Id);
        }

        _cache.Set(key, map, UsersCacheTtl);
        return map;
    }

    /// <summary>Nome real do usuário Kommo, caindo no ID quando não encontrado.</summary>
    private static string ResolveAgente(Dictionary<long, string> userNames, long userId)
        => userNames.TryGetValue(userId, out var name) ? name : $"Kommo · user {userId}";
}
