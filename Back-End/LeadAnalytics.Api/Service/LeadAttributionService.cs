using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace LeadAnalytics.Api.Service;

/// <summary>
/// Serviço responsável pela atribuição de origem dos leads
/// usando eventos da Meta como fonte da verdade
/// </summary>
public class LeadAttributionService(AppDbContext db, ILogger<LeadAttributionService> logger)
{
    private readonly AppDbContext _db = db;
    private readonly ILogger<LeadAttributionService> _logger = logger;

    /// <summary>
    /// Busca o MELHOR OriginEvent disponível para um telefone
    /// Prioridade: AD > SOCIAL > UNKNOWN
    /// Janela: últimas 24 horas
    /// </summary>
    public async Task<OriginEvent?> FindBestOriginEventAsync(string normalizedPhone, int tenantId)
    {
        var cutoff = DateTime.UtcNow.AddHours(-24);

        // 1️⃣ Busca TODOS os eventos não processados (executa no banco)
        var events = await _db.OriginEvents
            .Where(e =>
                e.Phone == normalizedPhone &&
                !e.Processed &&
                e.TenantId == tenantId &&
                e.ReceivedAt >= cutoff)
            .ToListAsync();

        // 2️⃣ Ordena em memória (C#, não SQL)
        var bestEvent = events
            .OrderByDescending(e => GetSourceTypePriority(e.SourceType))
            .ThenByDescending(e => e.ReceivedAt)
            .FirstOrDefault();

        if (bestEvent is not null)
        {
            if (_logger.IsEnabled(LogLevel.Information))
                _logger.LogInformation(
                    "🎯 Evento encontrado: {Phone} → {SourceType} (ID: {EventId})",
                    normalizedPhone, bestEvent.SourceType, bestEvent.Id);
        }
        else
        {
            _logger.LogWarning(
                "⚠️ Nenhum evento Meta encontrado para {Phone} (Tenant: {TenantId})",
                normalizedPhone, tenantId);
        }

        return bestEvent;
    }

    /// <summary>
    /// Busca TODOS os OriginEvents disponíveis para um telefone
    /// Útil para análise e debug
    /// </summary>
    public async Task<List<OriginEvent>> FindAllOriginEventsAsync(string normalizedPhone, int tenantId)
    {
        var cutoff = DateTime.UtcNow.AddHours(-24);

        return await _db.OriginEvents
            .Where(e =>
                e.Phone == normalizedPhone &&
                e.TenantId == tenantId &&
                e.ReceivedAt >= cutoff)
            .OrderByDescending(e => e.ReceivedAt)
            .ToListAsync();
    }

    /// <summary>
    /// Cria LeadAttribution e marca OriginEvent como processado
    /// </summary>
    public async Task<LeadAttribution> CreateAttributionAsync(
        int leadId,
        OriginEvent originEvent,
        string normalizedPhone,
        int tenantId)
    {
        var matchType = DetermineMatchType(originEvent.SourceType);

        var attribution = new LeadAttribution
        {
            LeadId = leadId,
            Phone = normalizedPhone,
            CtwaClid = originEvent.CtwaClid,
            SourceId = originEvent.SourceId,
            SourceType = originEvent.SourceType,
            MatchType = matchType,
            Confidence = originEvent.Confidence,
            OriginEventId = originEvent.Id,
            TenantId = tenantId,
            MatchedAt = DateTime.UtcNow
        };

        _db.LeadAttributions.Add(attribution);

        // 🔥 Marca evento como processado
        originEvent.Processed = true;

        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "✅ LeadAttribution criado: Lead {LeadId} ← OriginEvent {EventId} ({SourceType}, {Confidence})",
            leadId, originEvent.Id, originEvent.SourceType, originEvent.Confidence);

        return attribution;
    }

    /// <summary>
    /// Verifica se deve tentar melhorar a atribuição do lead
    /// </summary>
    public bool ShouldTryImproveAttribution(Lead lead)
    {
        var shouldTry = lead.Source == "DESCONHECIDO" ||
                        lead.TrackingConfidence == "BAIXA" ||
                        lead.TrackingConfidence == "MEDIA";

        if (shouldTry)
        {
            _logger.LogInformation(
                "🔄 Lead {LeadId} elegível para melhoria de atribuição (Source: {Source}, Confidence: {Confidence})",
                lead.Id, lead.Source, lead.TrackingConfidence);
        }

        return shouldTry;
    }

    /// <summary>
    /// Verifica se o evento é MELHOR que a atribuição atual do lead
    /// Regra: AD > SOCIAL > UNKNOWN
    /// </summary>
    public bool IsEventBetter(OriginEvent evento, Lead lead)
    {
        var eventoRank = GetSourceRank(evento.SourceType);
        var leadRank = GetSourceRank(lead.Source);

        // Evento é melhor se:
        // 1. Tipo de origem é superior (AD > SOCIAL > UNKNOWN)
        var isBetterType = eventoRank > leadRank;

        // 2. Mesmo tipo mas confiança superior
        var isSameTypeButBetterConfidence =
            eventoRank == leadRank &&
            IsConfidenceBetter(evento.Confidence, lead.TrackingConfidence);

        var isBetter = isBetterType || isSameTypeButBetterConfidence;

        if (isBetter)
        {
            _logger.LogInformation(
                "⬆️ Evento {EventId} é MELHOR que atribuição atual do Lead {LeadId}: " +
                "{EventSource}/{EventConf} > {LeadSource}/{LeadConf}",
                evento.Id, lead.Id,
                evento.SourceType, evento.Confidence,
                lead.Source, lead.TrackingConfidence);
        }

        return isBetter;
    }

    /// <summary>
    /// Extrai dados de atribuição de um OriginEvent
    /// </summary>
    public AttributionData ExtractAttributionData(OriginEvent originEvent)
    {
        return new AttributionData
        {
            Source = originEvent.SourceType ?? "DESCONHECIDO",
            Campaign = originEvent.SourceId ?? "DESCONHECIDO",
            Ad = originEvent.Headline,
            Confidence = originEvent.Confidence
        };
    }

    /// <summary>
    /// Retorna prioridade numérica do tipo de origem
    /// 3 = AD (maior prioridade)
    /// 2 = SOCIAL
    /// 1 = UNKNOWN (menor prioridade)
    /// </summary>
    private static int GetSourceTypePriority(string? sourceType)
    {
        return sourceType switch
        {
            "AD" => 3,
            "SOCIAL" => 2,
            _ => 1
        };
    }

    /// <summary>
    /// Retorna rank da origem (para comparação)
    /// </summary>
    private static int GetSourceRank(string? source)
    {
        return source switch
        {
            "AD" => 3,
            "SOCIAL" => 2,
            "DESCONHECIDO" => 1,
            _ => 1
        };
    }

    /// <summary>
    /// Verifica se a confiança A é melhor que B
    /// </summary>
    private static bool IsConfidenceBetter(string confidenceA, string confidenceB)
    {
        var rankA = confidenceA switch
        {
            "HIGH" or "ALTA" => 3,
            "MEDIUM" or "MEDIA" => 2,
            "LOW" or "BAIXA" => 1,
            _ => 0
        };

        var rankB = confidenceB switch
        {
            "HIGH" or "ALTA" => 3,
            "MEDIUM" or "MEDIA" => 2,
            "LOW" or "BAIXA" => 1,
            _ => 0
        };

        return rankA > rankB;
    }

    /// <summary>
    /// Determina o tipo de match baseado no SourceType
    /// </summary>
    private static string DetermineMatchType(string? sourceType)
    {
        return sourceType switch
        {
            "AD" => "CTWA",
            "SOCIAL" => "SOCIAL",
            _ => "UNKNOWN"
        };
    }

    /// <summary>
    /// Normaliza telefone: remove tudo que não é dígito
    /// </summary>
    public static string NormalizePhone(string phone)
    {
        return new string([.. phone.Where(char.IsDigit)]);
    }
}

// ═══════════════════════════════════════════════════════════════
// 📦 DTOs
// ═══════════════════════════════════════════════════════════════

/// <summary>
/// Dados extraídos de um OriginEvent para atribuição
/// </summary>
public record AttributionData
{
    public string Source { get; init; } = "DESCONHECIDO";
    public string Campaign { get; init; } = "DESCONHECIDO";
    public string? Ad { get; init; }
    public string Confidence { get; init; } = "BAIXA";
}