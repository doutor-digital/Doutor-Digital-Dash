namespace LeadAnalytics.Api.Service;

/// <summary>
/// Segmentos de negócio suportados pelo dashboard. O segmento de um <see cref="Models.Unit"/>
/// define qual conjunto de KPIs e qual caminho de cálculo o dashboard usa — o layout
/// (componentes de card/gráfico/drill-down) é compartilhado entre todos.
/// </summary>
public static class Segments
{
    /// <summary>Clínicas médicas/odontológicas (padrão histórico — ex.: Doutor Hérnia).</summary>
    public const string Saude = "saude";

    /// <summary>Escritórios de advocacia (ex.: Advocacia Magalhães).</summary>
    public const string Juridico = "juridico";

    public static readonly string[] All = { Saude, Juridico };

    public static bool IsValid(string? segment) =>
        segment is not null && Array.Exists(All, s => s == segment);

    /// <summary>Normaliza um valor de entrada para um segmento válido (fallback em <see cref="Saude"/>).</summary>
    public static string Normalize(string? segment) =>
        IsValid(segment) ? segment! : Saude;
}
