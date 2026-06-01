namespace LeadAnalytics.Api.DTOs.Auth;

public class GeoConsentRequestDto
{
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double? Accuracy { get; set; }
}
