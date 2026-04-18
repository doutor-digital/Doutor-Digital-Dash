namespace LeadAnalytics.Api.DTOs.Response;

public class EvolutionAdvancedDto
{
    public DateTime StartDateLocal { get; set; }
    public DateTime EndDateLocal { get; set; }
    public int? ClinicId { get; set; }

    public int TotalLeads { get; set; }
    public double AverageMonthly { get; set; }
    public double MedianMonthly { get; set; }
    public double StdDevMonthly { get; set; }
    public int BestMonthTotal { get; set; }
    public string BestMonthLabel { get; set; } = string.Empty;
    public int WorstMonthTotal { get; set; }
    public string WorstMonthLabel { get; set; } = string.Empty;
    public double GrowthPercentFirstToLast { get; set; }

    public List<EvolutionMonthPointDto> Monthly { get; set; } = new();
    public List<EvolutionWeekdayDto> Weekday { get; set; } = new();
    public List<EvolutionHourDto> Hour { get; set; } = new();
    public List<EvolutionSourceSerieDto> SourcesOverTime { get; set; } = new();
    public List<EvolutionConversionPointDto> ConversionOverTime { get; set; } = new();
}

public class EvolutionMonthPointDto
{
    public int Year { get; set; }
    public int Month { get; set; }
    public string Label { get; set; } = string.Empty;
    public int Total { get; set; }
    public int Cumulative { get; set; }
    public double? MomGrowthPercent { get; set; }
    public double? MovingAverage3 { get; set; }
}

public class EvolutionWeekdayDto
{
    public int Weekday { get; set; }
    public string Label { get; set; } = string.Empty;
    public int Total { get; set; }
}

public class EvolutionHourDto
{
    public int Hour { get; set; }
    public int Total { get; set; }
}

public class EvolutionSourceSerieDto
{
    public string Source { get; set; } = "DESCONHECIDO";
    public int Total { get; set; }
    public List<EvolutionSourceMonthDto> Points { get; set; } = new();
}

public class EvolutionSourceMonthDto
{
    public int Year { get; set; }
    public int Month { get; set; }
    public string Label { get; set; } = string.Empty;
    public int Count { get; set; }
}

public class EvolutionConversionPointDto
{
    public int Year { get; set; }
    public int Month { get; set; }
    public string Label { get; set; } = string.Empty;
    public int Total { get; set; }
    public int Agendado { get; set; }
    public int Pago { get; set; }
    public int Tratamento { get; set; }
    public double AgendadoRate { get; set; }
    public double PagoRate { get; set; }
}
