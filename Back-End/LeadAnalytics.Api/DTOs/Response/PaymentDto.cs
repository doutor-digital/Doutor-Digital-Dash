namespace LeadAnalytics.Api.DTOs.Response;

public class PaymentCreateDto
{
    public int LeadId { get; set; }
    public int ClinicId { get; set; }

    public string Treatment { get; set; } = null!;
    public int TreatmentDurationMonths { get; set; }
    public decimal? TreatmentValue { get; set; }

    public string? PaymentMethod { get; set; }
    public decimal DownPayment { get; set; }
    public int Installments { get; set; } = 1;

    public DateTime? PaidAt { get; set; }
    public string? Notes { get; set; }

    public List<PaymentSplitInputDto>? Splits { get; set; }
}

public class PaymentSplitInputDto
{
    public string PaymentMethod { get; set; } = null!;
    public decimal Amount { get; set; }
    public int Installments { get; set; } = 1;
    public string? Notes { get; set; }
}

public class PaymentSplitDto
{
    public int Id { get; set; }
    public string PaymentMethod { get; set; } = null!;
    public decimal Amount { get; set; }
    public int Installments { get; set; } = 1;
    public decimal InstallmentValue { get; set; }
    public string? Notes { get; set; }
}

public class PaymentResponseDto
{
    public int Id { get; set; }
    public int LeadId { get; set; }
    public string LeadName { get; set; } = null!;
    public int? UnitId { get; set; }
    public string? UnitName { get; set; }

    public string Treatment { get; set; } = null!;
    public int TreatmentDurationMonths { get; set; }
    public decimal TreatmentValue { get; set; }

    public string PaymentMethod { get; set; } = null!;
    public decimal DownPayment { get; set; }
    public int Installments { get; set; }
    public decimal InstallmentValue { get; set; }
    public decimal Amount { get; set; }

    public string? Notes { get; set; }
    public DateTime PaidAt { get; set; }
    public DateTime CreatedAt { get; set; }

    public List<PaymentSplitDto> Splits { get; set; } = [];
}

public class PaymentMethodBreakdownDto
{
    public string PaymentMethod { get; set; } = null!;
    public int Quantity { get; set; }
    public decimal Total { get; set; }
}

public class UnitRevenueDto
{
    public int UnitId { get; set; }
    public int ClinicId { get; set; }
    public string UnitName { get; set; } = null!;
    public int PaymentsCount { get; set; }
    public decimal TotalRevenue { get; set; }
    public decimal TotalDownPayment { get; set; }
    public decimal PendingBalance { get; set; }
    public IReadOnlyList<PaymentMethodBreakdownDto> ByMethod { get; set; } = [];
}

public class RevenueSummaryDto
{
    public decimal GrandTotal { get; set; }
    public int TotalPayments { get; set; }
    public IReadOnlyList<UnitRevenueDto> Units { get; set; } = [];
}

public static class TreatmentCatalog
{
    public static readonly IReadOnlyList<TreatmentOptionDto> Options =
    [
        new() { Key = "clareamento",   Name = "Clareamento Dental",  DefaultDurationMonths = 1,  DefaultValue = 3800m },
        new() { Key = "ortodontia",    Name = "Ortodontia",           DefaultDurationMonths = 18, DefaultValue = 3800m },
        new() { Key = "implante",      Name = "Implante Dentário",    DefaultDurationMonths = 6,  DefaultValue = 3800m },
        new() { Key = "proteses",      Name = "Próteses",             DefaultDurationMonths = 3,  DefaultValue = 3800m },
        new() { Key = "lentes",        Name = "Lentes de Contato Dental", DefaultDurationMonths = 2, DefaultValue = 3800m },
        new() { Key = "canal",         Name = "Tratamento de Canal",  DefaultDurationMonths = 1,  DefaultValue = 3800m },
        new() { Key = "limpeza",       Name = "Limpeza / Profilaxia", DefaultDurationMonths = 1,  DefaultValue = 3800m },
        new() { Key = "harmonizacao",  Name = "Harmonização Facial",  DefaultDurationMonths = 2,  DefaultValue = 3800m },
    ];
}

public class TreatmentOptionDto
{
    public string Key { get; set; } = null!;
    public string Name { get; set; } = null!;
    public int DefaultDurationMonths { get; set; }
    public decimal DefaultValue { get; set; }
}
