namespace LeadAnalytics.Api.DTOs.Sync;

/// <summary>Unidade ativa elegível para sync Kommo (para o n8n iterar).</summary>
public class KommoSyncUnitDto
{
    public int UnitId { get; set; }
    public string? Name { get; set; }
}

/// <summary>Resultado do sync de UMA unidade Kommo.</summary>
public class KommoUnitSyncResultDto
{
    public int UnitId { get; set; }
    public string? Name { get; set; }
    public int LeadsFetched { get; set; }
    public int LeadsPersisted { get; set; }
    public int DurationMs { get; set; }
    public string? Error { get; set; }
}

/// <summary>Conta de Ads conectada (para o n8n iterar).</summary>
public class AdAccountRefDto
{
    public int AccountId { get; set; }
    public string? Name { get; set; }
    public string? Provider { get; set; }
}

/// <summary>Resultado do sync de gasto de UMA conta de Ads.</summary>
public class AdAccountSyncResultDto
{
    public int AccountId { get; set; }
    public string? Name { get; set; }
    public string? Provider { get; set; }
    public int RowsUpserted { get; set; }
    public string? Error { get; set; }
}
