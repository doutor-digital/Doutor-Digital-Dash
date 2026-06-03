namespace LeadAnalytics.Api.Models;

/// <summary>
/// Uma conta de anúncios (Meta Ads ou Google Ads) conectada por uma clínica na Central de
/// Integrações. Guarda os tokens de acesso CRIPTOGRAFADOS (via DataProtection) — nunca são
/// expostos em DTOs de leitura. O job <see cref="Service.Ads.AdsSpendSyncService"/> usa
/// estes tokens pra puxar o gasto por campanha/dia da API do provedor.
/// </summary>
public class AdAccount
{
    public int Id { get; set; }

    /// <summary>Tenant (clínica) dono da conexão.</summary>
    public int ClinicId { get; set; }

    /// <summary>Unidade específica (opcional — null = vale para o tenant inteiro).</summary>
    public int? UnitId { get; set; }

    /// <summary><c>meta</c> | <c>google</c>.</summary>
    public string Provider { get; set; } = "meta";

    /// <summary>Id da conta de anúncios no provedor (ex.: act_123 / customer id do Google).</summary>
    public string? ExternalAccountId { get; set; }

    /// <summary>Nome amigável da conta (vem do provedor).</summary>
    public string? Name { get; set; }

    /// <summary><c>connected</c> | <c>disconnected</c>.</summary>
    public string Status { get; set; } = "connected";

    /// <summary>Access token CRIPTOGRAFADO (DataProtection). Nunca exposto.</summary>
    public string? AccessTokenEnc { get; set; }

    /// <summary>Refresh token CRIPTOGRAFADO (DataProtection). Nunca exposto.</summary>
    public string? RefreshTokenEnc { get; set; }

    public DateTime? TokenExpiresAt { get; set; }

    /// <summary>Último sync bem-sucedido do gasto.</summary>
    public DateTime? LastSyncAt { get; set; }

    /// <summary>Resumo do último sync (ex.: "60 linhas" ou "erro: …").</summary>
    public string? LastSyncNote { get; set; }

    public string? UpdatedByEmail { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
