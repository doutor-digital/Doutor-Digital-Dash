using System.Text.Json.Serialization;

namespace LeadAnalytics.Api.DTOs.Dashboard;

/// <summary>Perfil avançado do lead pra dashboard principal.</summary>
public class LeadProfileAnalyticsDto
{
    [JsonPropertyName("total_leads")] public int TotalLeads { get; set; }
    public AgeBySegmentDto Age { get; set; } = new();
    public List<UpcomingApptDto> Upcoming { get; set; } = new();
    public List<LabelCountDto> Doctors { get; set; } = new();
    public OutcomesDto Outcomes { get; set; } = new();
}

/// <summary>Idade média por desfecho.</summary>
public class AgeBySegmentDto
{
    public AgeStatDto Overall { get; set; } = new();
    public AgeStatDto Agendou { get; set; } = new();
    public AgeStatDto Compareceu { get; set; } = new();
    public AgeStatDto Fechou { get; set; } = new();
    public AgeStatDto Faltou { get; set; } = new();
}

public class AgeStatDto
{
    /// <summary>Idade média (0 = sem amostra).</summary>
    public double Avg { get; set; }
    /// <summary>Quantos leads do segmento têm idade computável.</summary>
    public int Count { get; set; }
}

/// <summary>Lead com agendamento chegando (alerta/notificação).</summary>
public class UpcomingApptDto
{
    [JsonPropertyName("lead_id")] public int LeadId { get; set; }
    public string Name { get; set; } = "";
    public string? Phone { get; set; }
    [JsonPropertyName("scheduled_at")] public DateTime ScheduledAt { get; set; }
    [JsonPropertyName("days_until")] public int DaysUntil { get; set; }
}

public class LabelCountDto
{
    public string Label { get; set; } = "";
    public int Count { get; set; }
}

/// <summary>Contagem por desfecho (mini-funil).</summary>
public class OutcomesDto
{
    public int Contato { get; set; }
    public int Agendou { get; set; }
    public int Compareceu { get; set; }
    public int Fechou { get; set; }
    public int Faltou { get; set; }
}
