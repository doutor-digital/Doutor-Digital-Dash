namespace LeadAnalytics.Api.Options;

public class AuthOptions
{
    public List<string> SuperAdminEmails { get; set; } = [];

    // Opcional para produção: variável única com emails separados por vírgula.
    public string? SuperAdminEmailsCsv { get; set; }
}
