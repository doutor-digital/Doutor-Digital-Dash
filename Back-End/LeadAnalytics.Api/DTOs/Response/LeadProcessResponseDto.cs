using LeadAnalytics.Api.Service;

namespace LeadAnalytics.Api.DTOs.Response;

public record LeadProcessResponseDto
{
    public ProcessResult Result { get; init; }
    public int? LeadId { get; init; }
    public string? Message { get; init; }
    public string? Source { get; init; }
    public string? TrackingConfidence { get; init; }
}