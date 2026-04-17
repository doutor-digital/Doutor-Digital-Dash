using System.Text.Json.Serialization;

namespace LeadAnalytics.Api.DTOs.Cloudia;

public class CloudiaAssigmentUser
{
    [JsonPropertyName("assigned_user_id")]
    public int? AssignedUserId { get; set; }

    [JsonPropertyName("assigned_user_name")]
    public string? AssignedUserName { get; set; }

    [JsonPropertyName("assigned_user_email")]
    public string? AssignedUserEmail { get; set; }

    [JsonPropertyName("customer")]
    public CloudiaAssignmentCustomerDto? Customer { get; set; }
}

public class CloudiaAssignmentCustomerDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("clinic_id")]
    public int ClinicId { get; set; }

    [JsonPropertyName("stage")]
    public string? Stage { get; set; }
}
