namespace LeadAnalytics.Api.DTOs.Response;

public class ConversionAnalyticsDto
{
    public DateTime DateFrom { get; set; }
    public DateTime DateTo { get; set; }

    public int TotalEntradas { get; set; }
    public int TotalConvertidos { get; set; }
    public int TotalNaoConvertidos { get; set; }
    public int TotalEmAndamento { get; set; }

    public double TaxaConversao { get; set; }
    public double TaxaNaoConversao { get; set; }

    public double? MediaDiasAteConversao { get; set; }
    public double? MedianaDiasAteConversao { get; set; }

    public List<NaoConversaoMotivoDto> Motivos { get; set; } = new();
    public List<NaoConvertidoItemDto> Exemplos { get; set; } = new();
    public List<ConversaoFunilEtapaDto> Funil { get; set; } = new();
}

public class NaoConversaoMotivoDto
{
    public string Motivo { get; set; } = "";
    public string Categoria { get; set; } = "";
    public int Quantidade { get; set; }
    public double Percentual { get; set; }
    public List<string> PalavrasChave { get; set; } = new();
}

public class NaoConvertidoItemDto
{
    public int LeadId { get; set; }
    public string Name { get; set; } = "";
    public string? Phone { get; set; }
    public string CurrentStage { get; set; } = "";
    public string? Observations { get; set; }
    public string? MotivoCategoria { get; set; }
    public string? Source { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class ConversaoFunilEtapaDto
{
    public string Stage { get; set; } = "";
    public int Quantidade { get; set; }
}
