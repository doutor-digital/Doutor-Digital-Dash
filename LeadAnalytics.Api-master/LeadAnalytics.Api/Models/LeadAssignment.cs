namespace LeadAnalytics.Api.Models;

public class LeadAssignment
{
    public int Id { get; set; }
    // Gerado automaticamente pelo banco

    public int LeadId { get; set; }
    // Qual lead foi atribuído

    public int AttendantId { get; set; }
    // Qual atendente recebeu

    public string? Stage { get; set; }
    // Etapa do lead no momento da atribuição

    public DateTime AssignedAt { get; set; }
    // Quando foi atribuído

    public Lead Lead { get; set; } = null!;
    public Attendant Attendant { get; set; } = null!;
}
