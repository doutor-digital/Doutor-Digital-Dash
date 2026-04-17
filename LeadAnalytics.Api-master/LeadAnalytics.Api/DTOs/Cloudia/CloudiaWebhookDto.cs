namespace LeadAnalytics.Api.DTOs.Cloudia;

using System.Text.Json.Serialization;

public class CloudiaWebhookDto
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = null!;

    [JsonPropertyName("data")]
    public CloudiaLeadDataDto? Data { get; set; }

    // Para USER_ASSIGNED_TO_CUSTOMER
    [JsonPropertyName("customer")]
    public CloudiaLeadDataDto? Customer { get; set; }

    [JsonPropertyName("assigned_user_id")]
    public int? AssignedUserId { get; set; }

    [JsonPropertyName("assigned_user_name")]
    public string? AssignedUserName { get; set; }

    [JsonPropertyName("assigned_user_email")]
    public string? AssignedUserEmail { get; set; }
}
public class CloudiaCustomerDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("clinic_id")]
    public int ClinicId { get; set; }

    [JsonPropertyName("stage")]
    public string? Stage { get; set; }
}