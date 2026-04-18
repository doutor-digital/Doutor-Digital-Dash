namespace LeadAnalytics.Api.Models;

public class Contact
{
    public int Id { get; set; }
    public int TenantId { get; set; }

    public string Name { get; set; } = null!;
    public string PhoneNormalized { get; set; } = null!;
    public string? PhoneRaw { get; set; }

    // "webhook_cloudia" | "import_csv" | "manual"
    public string Origem { get; set; } = "import_csv";
    public int? ImportBatchId { get; set; }
    public ImportBatch? ImportBatch { get; set; }
    public DateTime? ImportedAt { get; set; }

    public string? Conexao { get; set; }
    public string? Observacoes { get; set; }
    public string? TagsJson { get; set; }
    public string? Etapa { get; set; }
    public string? MetaAdsIdsJson { get; set; }

    public DateTime? ConsultationAt { get; set; }
    public DateTime? ConsultationRegisteredAt { get; set; }
    public DateTime? LastMessageAt { get; set; }
    public DateTime? Birthday { get; set; }

    public bool Blocked { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
