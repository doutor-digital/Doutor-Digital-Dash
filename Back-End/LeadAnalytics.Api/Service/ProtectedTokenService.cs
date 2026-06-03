using Microsoft.AspNetCore.DataProtection;

namespace LeadAnalytics.Api.Service;

/// <summary>
/// Criptografa/descriptografa tokens sensíveis (OAuth de Ads) antes de gravar no banco,
/// usando a infra de DataProtection já configurada no Program.cs
/// (<c>AddDataProtection().PersistKeysToDbContext</c>). Assim os access/refresh tokens
/// nunca ficam legíveis na coluna.
/// </summary>
public class ProtectedTokenService(IDataProtectionProvider provider)
{
    private readonly IDataProtector _protector = provider.CreateProtector("LeadAnalytics.Api.Ads.Tokens.v1");

    /// <summary>Cifra um texto (no-op se nulo/vazio).</summary>
    public string? Protect(string? plaintext) =>
        string.IsNullOrEmpty(plaintext) ? plaintext : _protector.Protect(plaintext);

    /// <summary>Decifra um texto. Devolve null se o valor não puder ser decifrado.</summary>
    public string? Unprotect(string? encrypted)
    {
        if (string.IsNullOrEmpty(encrypted)) return encrypted;
        try { return _protector.Unprotect(encrypted); }
        catch { return null; }
    }
}
