namespace LeadAnalytics.Api.Models;

/// <summary>
/// Credenciais do APP de anúncios (Meta/Google) configuradas pelo analista na Central de
/// Integrações — globais por provedor (um app por plataforma; o OAuth depois conecta as
/// contas das clínicas). O segredo é guardado CRIPTOGRAFADO (DataProtection). Quando existe
/// uma linha aqui, ela tem prioridade sobre as variáveis de ambiente (<c>Ads:Meta</c>/<c>Ads:Google</c>).
/// </summary>
public class AdsSetting
{
    public int Id { get; set; }

    /// <summary><c>meta</c> | <c>google</c> (único).</summary>
    public string Provider { get; set; } = "meta";

    /// <summary>App ID (Meta) / OAuth Client ID (Google).</summary>
    public string? ClientId { get; set; }

    /// <summary>App Secret (Meta) / Client Secret (Google) — CRIPTOGRAFADO.</summary>
    public string? ClientSecretEnc { get; set; }

    /// <summary>Developer token (só Google Ads).</summary>
    public string? DeveloperToken { get; set; }

    public string? UpdatedByEmail { get; set; }
    public DateTime UpdatedAt { get; set; }
}
