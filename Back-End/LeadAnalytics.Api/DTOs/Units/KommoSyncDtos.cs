namespace LeadAnalytics.Api.DTOs.Units;

/// <summary>Body do POST /units/{id}/sync-from-kommo.</summary>
public class KommoSyncRequestDto
{
    /// <summary>
    /// Token de longa duração da Kommo (opcional se a unidade já tiver um salvo).
    /// Se vier preenchido, é salvo na unidade pra próximas sincronizações.
    /// </summary>
    public string? AccessToken { get; set; }

    /// <summary>Salva o <c>AccessToken</c> na unidade quando true (default true).</summary>
    public bool PersistToken { get; set; } = true;

    /// <summary>Limite de leads pra puxar (default 5000, máx 20000).</summary>
    public int? MaxLeads { get; set; }
}

public class KommoSyncResponseDto
{
    public bool Success { get; set; }
    public string? Error { get; set; }

    public int PagesFetched { get; set; }
    public int LeadsFetched { get; set; }
    public int ContactsFetched { get; set; }
    public int LeadsPersisted { get; set; }
    public int DurationMs { get; set; }
}
