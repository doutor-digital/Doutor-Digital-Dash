using System.Text.Json;
using LeadAnalytics.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace LeadAnalytics.Api.Service.Juridico;

/// <summary>
/// Mapeamento dos papéis semânticos do dashboard jurídico para os IDs de custom field
/// da conta Kommo do escritório. É per-unidade e fica guardado em <c>kpi_configurations</c>
/// (uma linha por papel, com <see cref="KpiKey"/> = uma das constantes <c>Juridico*Key</c> e
/// <c>ConfigJson = {"fieldId":123}</c>), exatamente como o mapeamento de Perfil do Lead.
///
/// O analista preenche esses IDs nas Configurações Técnicas; enquanto um papel não estiver
/// mapeado, o KPI correspondente cai num fallback (campo por nome) ou retorna vazio.
/// </summary>
public sealed class JuridicoFieldMap
{
    /// <summary>Área / tipo de caso do lead (ex.: Trabalhista, Cível, Criminal).</summary>
    public long? AreaCasoFieldId { get; set; }

    /// <summary>Quem qualificou o lead (ex.: "IA", "Humano") — base da qualidade da IA.</summary>
    public long? QualificadoPorFieldId { get; set; }

    /// <summary>Quem agendou (IA × secretária).</summary>
    public long? AgendadoPorFieldId { get; set; }

    /// <summary>Secretária/atendente responsável pelo lead.</summary>
    public long? ResponsavelFieldId { get; set; }

    /// <summary>Status de qualificação (Qualificado × Desqualificado).</summary>
    public long? StatusQualificacaoFieldId { get; set; }

    /// <summary>Motivo de desqualificação (qualidade da mídia por criativo).</summary>
    public long? MotivoDesqualificacaoFieldId { get; set; }

    /// <summary>Motivo de perda (onde a secretária/IA perde o lead).</summary>
    public long? MotivoPerdaFieldId { get; set; }

    /// <summary>Contrato assinado (marcador de fechamento, quando não é etapa do funil).</summary>
    public long? ContratoAssinadoFieldId { get; set; }

    /// <summary>Valor estimado da causa (proxy de receita).</summary>
    public long? ValorEstimadoFieldId { get; set; }

    /// <summary>Honorário de êxito (receita real por caso).</summary>
    public long? HonorarioExitoFieldId { get; set; }

    /// <summary>tracking_id do criativo (atribuição de mídia).</summary>
    public long? TrackingIdFieldId { get; set; }

    /// <summary>Equipe / grupo de atendimento (SLA por grupo).</summary>
    public long? GrupoFieldId { get; set; }

    /// <summary>Data/registro do 1º contato (quando não derivado de CreatedAt).</summary>
    public long? PrimeiroContatoFieldId { get; set; }

    /// <summary>Data/registro da 1ª resposta (quando não derivado de interações).</summary>
    public long? PrimeiraRespostaFieldId { get; set; }

    // ─── Chaves (KpiKey) usadas nas linhas de kpi_configurations ───────────────
    public const string AreaCasoKey = "juridico_area_caso";
    public const string QualificadoPorKey = "juridico_qualificado_por";
    public const string AgendadoPorKey = "juridico_agendado_por";
    public const string ResponsavelKey = "juridico_responsavel";
    public const string StatusQualificacaoKey = "juridico_status_qualificacao";
    public const string MotivoDesqualificacaoKey = "juridico_motivo_desqualificacao";
    public const string MotivoPerdaKey = "juridico_motivo_perda";
    public const string ContratoAssinadoKey = "juridico_contrato_assinado";
    public const string ValorEstimadoKey = "juridico_valor_estimado";
    public const string HonorarioExitoKey = "juridico_honorario_exito";
    public const string TrackingIdKey = "juridico_tracking_id";
    public const string GrupoKey = "juridico_grupo";
    public const string PrimeiroContatoKey = "juridico_primeiro_contato";
    public const string PrimeiraRespostaKey = "juridico_primeira_resposta";

    public static readonly string[] AllKeys =
    {
        AreaCasoKey, QualificadoPorKey, AgendadoPorKey, ResponsavelKey,
        StatusQualificacaoKey, MotivoDesqualificacaoKey, MotivoPerdaKey,
        ContratoAssinadoKey, ValorEstimadoKey, HonorarioExitoKey,
        TrackingIdKey, GrupoKey, PrimeiroContatoKey, PrimeiraRespostaKey,
    };
}

/// <summary>Carrega o <see cref="JuridicoFieldMap"/> de uma unidade a partir de <c>kpi_configurations</c>.</summary>
public sealed class JuridicoFieldMapService(AppDbContext db)
{
    public async Task<JuridicoFieldMap> LoadAsync(int unitId, CancellationToken ct = default)
    {
        var rows = await db.KpiConfigurations.AsNoTracking()
            .Where(k => k.UnitId == unitId && JuridicoFieldMap.AllKeys.Contains(k.KpiKey))
            .ToListAsync(ct);

        long? FieldOf(string key)
        {
            var r = rows.FirstOrDefault(x => x.KpiKey == key);
            if (r is null || string.IsNullOrWhiteSpace(r.ConfigJson)) return null;
            try
            {
                var el = JsonSerializer.Deserialize<JsonElement>(r.ConfigJson);
                if (el.ValueKind == JsonValueKind.Object && el.TryGetProperty("fieldId", out var f))
                {
                    if (f.ValueKind == JsonValueKind.Number && f.TryGetInt64(out var n)) return n;
                    if (f.ValueKind == JsonValueKind.String && long.TryParse(f.GetString(), out var s)) return s;
                }
            }
            catch (JsonException) { /* ignora json inválido */ }
            return null;
        }

        return new JuridicoFieldMap
        {
            AreaCasoFieldId = FieldOf(JuridicoFieldMap.AreaCasoKey),
            QualificadoPorFieldId = FieldOf(JuridicoFieldMap.QualificadoPorKey),
            AgendadoPorFieldId = FieldOf(JuridicoFieldMap.AgendadoPorKey),
            ResponsavelFieldId = FieldOf(JuridicoFieldMap.ResponsavelKey),
            StatusQualificacaoFieldId = FieldOf(JuridicoFieldMap.StatusQualificacaoKey),
            MotivoDesqualificacaoFieldId = FieldOf(JuridicoFieldMap.MotivoDesqualificacaoKey),
            MotivoPerdaFieldId = FieldOf(JuridicoFieldMap.MotivoPerdaKey),
            ContratoAssinadoFieldId = FieldOf(JuridicoFieldMap.ContratoAssinadoKey),
            ValorEstimadoFieldId = FieldOf(JuridicoFieldMap.ValorEstimadoKey),
            HonorarioExitoFieldId = FieldOf(JuridicoFieldMap.HonorarioExitoKey),
            TrackingIdFieldId = FieldOf(JuridicoFieldMap.TrackingIdKey),
            GrupoFieldId = FieldOf(JuridicoFieldMap.GrupoKey),
            PrimeiroContatoFieldId = FieldOf(JuridicoFieldMap.PrimeiroContatoKey),
            PrimeiraRespostaFieldId = FieldOf(JuridicoFieldMap.PrimeiraRespostaKey),
        };
    }
}
