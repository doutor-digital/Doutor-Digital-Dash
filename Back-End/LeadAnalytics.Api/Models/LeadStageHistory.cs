namespace LeadAnalytics.Api.Models;

public class LeadStageHistory
{
    /// <summary>Entrada na etapa via webhook ao vivo da Kommo (lead:status). Data confiável (instante real).</summary>
    public const string SourceWebhook = "webhook";
    /// <summary>Entrada reconstruída a partir da API de eventos da Kommo (lead_status_changed). Data real (created_at do evento).</summary>
    public const string SourceEventsApi = "events_api";
    /// <summary>Linhas legadas gravadas pelo sync/heal com ChangedAt = updated_at (NÃO é a data de entrada na etapa). Excluídas das contagens por data.</summary>
    public const string SourceLegacy = "legacy";

    public int Id { get; set; }
    public int LeadId { get; set; }
    public Lead Lead { get; set; } = null!;
    public int StageId { get; set; }
    public string StageLabel { get; set; } = null!;

    /// <summary>
    /// Instante REAL da transição de etapa. Só é confiável quando <see cref="EntrySource"/>
    /// é <see cref="SourceWebhook"/> ou <see cref="SourceEventsApi"/>. Para <see cref="SourceLegacy"/>
    /// vale a última modificação do lead (updated_at), não a entrada na etapa.
    /// </summary>
    public DateTime ChangedAt { get; set; }

    /// <summary>
    /// Id do evento <c>lead_status_changed</c> na Kommo (ULID, ex.: "01ktrqv6w4b4h7z..."),
    /// quando a linha veio do backfill da API de eventos. Chave de deduplicação: reexecutar
    /// o backfill não duplica linhas. Null para linhas de webhook/legado.
    /// </summary>
    public string? KommoEventId { get; set; }

    /// <summary>
    /// Procedência da linha — define se a <see cref="ChangedAt"/> pode ser usada como data de
    /// entrada na etapa. Ver constantes <see cref="SourceWebhook"/>/<see cref="SourceEventsApi"/>/<see cref="SourceLegacy"/>.
    /// </summary>
    public string EntrySource { get; set; } = SourceWebhook;

    /// <summary>
    /// Data corrigida pelo admin quando a SDR moveu o lead na etapa fora do dia real
    /// (ex.: esqueceu de mover e fez no dia seguinte). Quando setada, os KPIs usam esta
    /// no lugar de <see cref="ChangedAt"/>. Null = sem correção; original vale.
    /// </summary>
    public DateTime? CorrectedChangedAt { get; set; }

    /// <summary>Quem aplicou a correção (id do usuário do nosso backend).</summary>
    public int? CorrectedByUserId { get; set; }

    /// <summary>Email de quem aplicou a correção (trilha de auditoria, sobrevive a delete de user).</summary>
    public string? CorrectedByEmail { get; set; }

    /// <summary>Quando a correção foi feita.</summary>
    public DateTime? CorrectedAt { get; set; }

    /// <summary>Motivo opcional preenchido pelo admin no momento da correção.</summary>
    public string? CorrectionReason { get; set; }
}
