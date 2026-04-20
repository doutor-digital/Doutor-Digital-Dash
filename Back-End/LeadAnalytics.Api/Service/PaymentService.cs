using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.DTOs.Response;
using LeadAnalytics.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace LeadAnalytics.Api.Service;

public class PaymentService(AppDbContext db, UnitService unitService, ILogger<PaymentService> logger)
{
    private readonly AppDbContext _db = db;
    private readonly UnitService _unitService = unitService;
    private readonly ILogger<PaymentService> _logger = logger;

    public static readonly string[] AllowedMethods =
        ["pix", "dinheiro", "debito", "credito", "boleto", "transferencia"];

    public async Task<PaymentResponseDto> CreateAsync(PaymentCreateDto dto, CancellationToken ct = default)
    {
        if (dto.LeadId <= 0)
            throw new ArgumentException("leadId inválido");
        if (dto.ClinicId <= 0)
            throw new ArgumentException("clinicId inválido");
        if (string.IsNullOrWhiteSpace(dto.Treatment))
            throw new ArgumentException("tratamento obrigatório");

        var hasSplits = dto.Splits is { Count: > 0 };

        if (!hasSplits && string.IsNullOrWhiteSpace(dto.PaymentMethod))
            throw new ArgumentException("forma de pagamento obrigatória");

        if (dto.Installments <= 0) dto.Installments = 1;
        if (dto.TreatmentDurationMonths < 0) dto.TreatmentDurationMonths = 0;

        var lead = await _db.Leads.FirstOrDefaultAsync(
            l => l.Id == dto.LeadId && l.TenantId == dto.ClinicId, ct)
            ?? throw new InvalidOperationException($"lead {dto.LeadId} não encontrado para clínica {dto.ClinicId}");

        var unit = await _unitService.GetOrCreateAsync(dto.ClinicId);

        var treatmentValue = dto.TreatmentValue is > 0
            ? dto.TreatmentValue.Value
            : Payment.DefaultTreatmentValue;

        Payment payment;

        if (hasSplits)
        {
            var normalizedSplits = new List<PaymentSplit>(dto.Splits!.Count);
            decimal totalFromSplits = 0m;

            foreach (var s in dto.Splits!)
            {
                if (string.IsNullOrWhiteSpace(s.PaymentMethod))
                    throw new ArgumentException("cada forma de pagamento precisa de método");

                var m = s.PaymentMethod.Trim().ToLowerInvariant();
                if (!AllowedMethods.Contains(m))
                    throw new ArgumentException($"forma de pagamento inválida: '{s.PaymentMethod}'. Use: {string.Join(", ", AllowedMethods)}");

                if (s.Amount <= 0)
                    throw new ArgumentException("valor de cada forma deve ser maior que zero");

                var inst = s.Installments <= 0 ? 1 : s.Installments;
                var instValue = Math.Round(s.Amount / inst, 2);

                normalizedSplits.Add(new PaymentSplit
                {
                    PaymentMethod = m,
                    Amount = s.Amount,
                    Installments = inst,
                    InstallmentValue = instValue,
                    Notes = s.Notes,
                    CreatedAt = DateTime.UtcNow,
                });

                totalFromSplits += s.Amount;
            }

            if (totalFromSplits <= 0)
                throw new ArgumentException("soma das formas de pagamento deve ser maior que zero");

            if (Math.Abs(totalFromSplits - treatmentValue) > 0.01m)
                throw new ArgumentException(
                    $"soma das formas de pagamento ({totalFromSplits:N2}) difere do valor do tratamento ({treatmentValue:N2})");

            payment = new Payment
            {
                LeadId = lead.Id,
                TenantId = dto.ClinicId,
                UnitId = unit.Id,
                Treatment = dto.Treatment.Trim(),
                TreatmentDurationMonths = dto.TreatmentDurationMonths,
                TreatmentValue = treatmentValue,
                PaymentMethod = PaymentMethodConstants.Composite,
                DownPayment = 0m,
                Installments = normalizedSplits.Max(s => s.Installments),
                InstallmentValue = 0m,
                Amount = totalFromSplits,
                Notes = dto.Notes,
                PaidAt = ToUtc(dto.PaidAt) ?? DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
                Splits = normalizedSplits,
            };
        }
        else
        {
            var method = dto.PaymentMethod!.Trim().ToLowerInvariant();
            if (!AllowedMethods.Contains(method))
                throw new ArgumentException($"forma de pagamento inválida. Use: {string.Join(", ", AllowedMethods)}");

            if (dto.DownPayment < 0 || dto.DownPayment > treatmentValue)
                throw new ArgumentException("entrada inválida (não pode ser negativa nem maior que o valor do tratamento)");

            var remaining = treatmentValue - dto.DownPayment;
            var installmentValue = dto.Installments > 0
                ? Math.Round(remaining / dto.Installments, 2)
                : 0m;

            payment = new Payment
            {
                LeadId = lead.Id,
                TenantId = dto.ClinicId,
                UnitId = unit.Id,
                Treatment = dto.Treatment.Trim(),
                TreatmentDurationMonths = dto.TreatmentDurationMonths,
                TreatmentValue = treatmentValue,
                PaymentMethod = method,
                DownPayment = dto.DownPayment,
                Installments = dto.Installments,
                InstallmentValue = installmentValue,
                Amount = treatmentValue,
                Notes = dto.Notes,
                PaidAt = ToUtc(dto.PaidAt) ?? DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
            };
        }

        _db.Payments.Add(payment);

        lead.HasPayment = true;
        lead.ConvertedAt ??= payment.PaidAt;
        lead.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "💰 Pagamento {Id} registrado: lead={LeadId} clinicId={Clinic} tratamento={Treatment} valor={Valor} formas={Formas}",
            payment.Id, lead.Id, dto.ClinicId, payment.Treatment, payment.Amount,
            hasSplits ? string.Join("+", payment.Splits.Select(s => s.PaymentMethod)) : payment.PaymentMethod);

        return ToDto(payment, lead.Name, unit.Name);
    }

    public async Task<IReadOnlyList<PaymentResponseDto>> ListByLeadAsync(
        int clinicId, int leadId, CancellationToken ct = default)
    {
        return await _db.Payments
            .AsNoTracking()
            .Where(p => p.TenantId == clinicId && p.LeadId == leadId)
            .OrderByDescending(p => p.PaidAt)
            .Select(p => new PaymentResponseDto
            {
                Id = p.Id,
                LeadId = p.LeadId,
                LeadName = p.Lead.Name,
                UnitId = p.UnitId,
                UnitName = p.Unit != null ? p.Unit.Name : null,
                Treatment = p.Treatment,
                TreatmentDurationMonths = p.TreatmentDurationMonths,
                TreatmentValue = p.TreatmentValue,
                PaymentMethod = p.PaymentMethod,
                DownPayment = p.DownPayment,
                Installments = p.Installments,
                InstallmentValue = p.InstallmentValue,
                Amount = p.Amount,
                Notes = p.Notes,
                PaidAt = p.PaidAt,
                CreatedAt = p.CreatedAt,
                Splits = p.Splits.Select(s => new PaymentSplitDto
                {
                    Id = s.Id,
                    PaymentMethod = s.PaymentMethod,
                    Amount = s.Amount,
                    Installments = s.Installments,
                    InstallmentValue = s.InstallmentValue,
                    Notes = s.Notes,
                }).ToList(),
            })
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<PaymentResponseDto>> ListByClinicAsync(
        int clinicId,
        DateTime? dateFrom,
        DateTime? dateTo,
        string? treatment,
        string? method,
        CancellationToken ct = default)
    {
        var q = _db.Payments.AsNoTracking().Where(p => p.TenantId == clinicId);

        var fromUtc = ToUtc(dateFrom);
        var toUtc = ToUtc(dateTo);

        if (fromUtc.HasValue) q = q.Where(p => p.PaidAt >= fromUtc.Value);
        if (toUtc.HasValue) q = q.Where(p => p.PaidAt < toUtc.Value);
        if (!string.IsNullOrWhiteSpace(treatment))
            q = q.Where(p => p.Treatment == treatment);
        if (!string.IsNullOrWhiteSpace(method))
        {
            var m = method.Trim().ToLowerInvariant();
            q = q.Where(p => p.PaymentMethod == m || p.Splits.Any(s => s.PaymentMethod == m));
        }

        return await q
            .OrderByDescending(p => p.PaidAt)
            .Select(p => new PaymentResponseDto
            {
                Id = p.Id,
                LeadId = p.LeadId,
                LeadName = p.Lead.Name,
                UnitId = p.UnitId,
                UnitName = p.Unit != null ? p.Unit.Name : null,
                Treatment = p.Treatment,
                TreatmentDurationMonths = p.TreatmentDurationMonths,
                TreatmentValue = p.TreatmentValue,
                PaymentMethod = p.PaymentMethod,
                DownPayment = p.DownPayment,
                Installments = p.Installments,
                InstallmentValue = p.InstallmentValue,
                Amount = p.Amount,
                Notes = p.Notes,
                PaidAt = p.PaidAt,
                CreatedAt = p.CreatedAt,
                Splits = p.Splits.Select(s => new PaymentSplitDto
                {
                    Id = s.Id,
                    PaymentMethod = s.PaymentMethod,
                    Amount = s.Amount,
                    Installments = s.Installments,
                    InstallmentValue = s.InstallmentValue,
                    Notes = s.Notes,
                }).ToList(),
            })
            .ToListAsync(ct);
    }

    public async Task<RevenueSummaryDto> GetRevenueByUnitAsync(
        int? clinicId,
        DateTime? dateFrom,
        DateTime? dateTo,
        CancellationToken ct = default)
    {
        var q = _db.Payments.AsNoTracking().AsQueryable();

        var fromUtc = ToUtc(dateFrom);
        var toUtc = ToUtc(dateTo);

        if (clinicId is > 0) q = q.Where(p => p.TenantId == clinicId.Value);
        if (fromUtc.HasValue) q = q.Where(p => p.PaidAt >= fromUtc.Value);
        if (toUtc.HasValue) q = q.Where(p => p.PaidAt < toUtc.Value);

        var grouped = await q
            .GroupBy(p => new { p.UnitId, p.TenantId })
            .Select(g => new
            {
                g.Key.UnitId,
                g.Key.TenantId,
                Count = g.Count(),
                Revenue = g.Sum(p => p.Amount),
                DownSum = g.Sum(p => p.DownPayment),
            })
            .ToListAsync(ct);

        var nonComposite = await q
            .Where(p => p.PaymentMethod != PaymentMethodConstants.Composite)
            .GroupBy(p => new { p.UnitId, p.PaymentMethod })
            .Select(g => new
            {
                g.Key.UnitId,
                g.Key.PaymentMethod,
                Quantity = g.Count(),
                Total = g.Sum(p => p.Amount),
            })
            .ToListAsync(ct);

        var splitBreakdown = await q
            .Where(p => p.PaymentMethod == PaymentMethodConstants.Composite)
            .SelectMany(p => p.Splits.Select(s => new { p.UnitId, s.PaymentMethod, s.Amount }))
            .GroupBy(x => new { x.UnitId, x.PaymentMethod })
            .Select(g => new
            {
                g.Key.UnitId,
                g.Key.PaymentMethod,
                Quantity = g.Count(),
                Total = g.Sum(x => x.Amount),
            })
            .ToListAsync(ct);

        var methodBreakdown = nonComposite
            .Concat(splitBreakdown)
            .GroupBy(x => new { x.UnitId, x.PaymentMethod })
            .Select(g => new
            {
                g.Key.UnitId,
                g.Key.PaymentMethod,
                Quantity = g.Sum(x => x.Quantity),
                Total = g.Sum(x => x.Total),
            })
            .ToList();

        var unitIds = grouped
            .Where(x => x.UnitId.HasValue)
            .Select(x => x.UnitId!.Value)
            .Distinct()
            .ToList();

        var units = await _db.Units
            .AsNoTracking()
            .Where(u => unitIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, ct);

        var list = grouped
            .Select(g =>
            {
                Unit? unit = g.UnitId.HasValue && units.TryGetValue(g.UnitId.Value, out var u) ? u : null;
                var methods = methodBreakdown
                    .Where(m => m.UnitId == g.UnitId)
                    .Select(m => new PaymentMethodBreakdownDto
                    {
                        PaymentMethod = m.PaymentMethod,
                        Quantity = m.Quantity,
                        Total = m.Total,
                    })
                    .OrderByDescending(m => m.Total)
                    .ToList();

                return new UnitRevenueDto
                {
                    UnitId = g.UnitId ?? 0,
                    ClinicId = unit?.ClinicId ?? g.TenantId,
                    UnitName = unit?.Name ?? $"Unidade {g.TenantId}",
                    PaymentsCount = g.Count,
                    TotalRevenue = g.Revenue,
                    TotalDownPayment = g.DownSum,
                    PendingBalance = g.Revenue - g.DownSum,
                    ByMethod = methods,
                };
            })
            .OrderByDescending(x => x.TotalRevenue)
            .ToList();

        return new RevenueSummaryDto
        {
            GrandTotal = list.Sum(x => x.TotalRevenue),
            TotalPayments = list.Sum(x => x.PaymentsCount),
            Units = list,
        };
    }

    public async Task<bool> DeleteAsync(int clinicId, int paymentId, CancellationToken ct = default)
    {
        var payment = await _db.Payments
            .FirstOrDefaultAsync(p => p.Id == paymentId && p.TenantId == clinicId, ct);
        if (payment is null) return false;

        _db.Payments.Remove(payment);

        var stillHas = await _db.Payments
            .AnyAsync(p => p.LeadId == payment.LeadId && p.Id != payment.Id, ct);
        if (!stillHas)
        {
            var lead = await _db.Leads.FirstOrDefaultAsync(l => l.Id == payment.LeadId, ct);
            if (lead is not null)
            {
                lead.HasPayment = false;
                lead.UpdatedAt = DateTime.UtcNow;
            }
        }

        await _db.SaveChangesAsync(ct);
        return true;
    }

    private static DateTime? ToUtc(DateTime? value)
    {
        if (!value.HasValue) return null;
        var dt = value.Value;
        return dt.Kind switch
        {
            DateTimeKind.Utc => dt,
            DateTimeKind.Local => dt.ToUniversalTime(),
            _ => DateTime.SpecifyKind(dt, DateTimeKind.Utc),
        };
    }

    private static PaymentResponseDto ToDto(Payment p, string leadName, string? unitName) => new()
    {
        Id = p.Id,
        LeadId = p.LeadId,
        LeadName = leadName,
        UnitId = p.UnitId,
        UnitName = unitName,
        Treatment = p.Treatment,
        TreatmentDurationMonths = p.TreatmentDurationMonths,
        TreatmentValue = p.TreatmentValue,
        PaymentMethod = p.PaymentMethod,
        DownPayment = p.DownPayment,
        Installments = p.Installments,
        InstallmentValue = p.InstallmentValue,
        Amount = p.Amount,
        Notes = p.Notes,
        PaidAt = p.PaidAt,
        CreatedAt = p.CreatedAt,
        Splits = p.Splits?.Select(s => new PaymentSplitDto
        {
            Id = s.Id,
            PaymentMethod = s.PaymentMethod,
            Amount = s.Amount,
            Installments = s.Installments,
            InstallmentValue = s.InstallmentValue,
            Notes = s.Notes,
        }).ToList() ?? [],
    };
}
