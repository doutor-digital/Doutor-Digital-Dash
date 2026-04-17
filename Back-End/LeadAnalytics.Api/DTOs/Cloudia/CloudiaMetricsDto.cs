using System.Text.Json.Serialization;

namespace LeadAnalytics.Api.DTOs.Cloudia;

public class CloudiaMetricsResponseDto
{
    [JsonPropertyName("metrics")]
    public CloudiaMetricsDto Metrics { get; set; } = null!;

    [JsonPropertyName("waitingInQueueList")]
    public List<CloudiaWaitingDto> WaitingInQueueList { get; set; } = new();

    [JsonPropertyName("waitingForResponseList")]
    public List<CloudiaWaitingDto> WaitingForResponseList { get; set; } = new();

    [JsonPropertyName("attendantsServicesList")]
    public List<CloudiaAttendantServiceDto> AttendantsServicesList { get; set; } = new();
}

public class CloudiaMetricsDto
{
    [JsonPropertyName("totalInService")]
    public int TotalInService { get; set; }

    [JsonPropertyName("totalInQueue")]
    public int TotalInQueue { get; set; }

    [JsonPropertyName("waitResponseTimeAvg")]
    public double WaitResponseTimeAvg { get; set; }

    [JsonPropertyName("waitFirstResponseTimeAvg")]
    public double WaitFirstResponseTimeAvg { get; set; }

    [JsonPropertyName("maxWaitFirstResponseTime")]
    public double MaxWaitFirstResponseTime { get; set; }
}

public class CloudiaWaitingDto
{
    [JsonPropertyName("ticketId")]
    public int TicketId { get; set; }

    [JsonPropertyName("customerId")]
    public int CustomerId { get; set; }

    [JsonPropertyName("customerName")]
    public string? CustomerName { get; set; }

    [JsonPropertyName("waitingInMinutes")]
    public double WaitingInMinutes { get; set; }

    [JsonPropertyName("attendantName")]
    public string? AttendantName { get; set; }

    [JsonPropertyName("event")]
    public string? Event { get; set; }
}

public class CloudiaAttendantServiceDto
{
    [JsonPropertyName("attendantId")]
    public int? AttendantId { get; set; }

    [JsonPropertyName("attendantName")]
    public string? AttendantName { get; set; }

    [JsonPropertyName("totalServices")]
    public int TotalServices { get; set; }

    [JsonPropertyName("totalWaitingForResponse")]
    public int TotalWaitingForResponse { get; set; }
}