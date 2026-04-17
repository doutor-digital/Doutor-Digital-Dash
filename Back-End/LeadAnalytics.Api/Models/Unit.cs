namespace LeadAnalytics.Api.Models;

public class Unit
{
    public int Id { get; set; }
    public int ClinicId { get; set; }
    public string Name { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<Lead> Leads { get; set; } = [];

}
