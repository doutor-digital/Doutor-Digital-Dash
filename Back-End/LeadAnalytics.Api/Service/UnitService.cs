using System.Globalization;
using System.Text;
using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.DTOs.Response;
using LeadAnalytics.Api.DTOs.Units;
using LeadAnalytics.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace LeadAnalytics.Api.Service;

public class UnitService(AppDbContext db, ILogger<UnitService> logger)
{
    private readonly AppDbContext _db = db;
    private readonly ILogger<UnitService> _logger = logger;

    /// <summary>Imagem padrão das unidades quando nenhuma foto é informada.</summary>
    public const string DefaultPhotoUrl = "https://stract.to/wp-content/uploads/2024/12/kommo-crm.png";

    public async Task<Unit> GetOrCreateAsync(int clinicId)
    {
        var unit = await _db.Units
            .FirstOrDefaultAsync(u => u.ClinicId == clinicId);

        if (unit is null)
        {
            var name = clinicId == 8020
                ? $"Unidade de Araguaína {clinicId}"
                : $"Unidade {clinicId}";

            unit = new Unit
            {
                ClinicId = clinicId,
                Name = name,
                Slug = await GenerateUniqueSlugAsync(name),
                PhotoUrl = DefaultPhotoUrl,
                CreatedAt = DateTime.UtcNow
            };

            _db.Units.Add(unit);
            await _db.SaveChangesAsync();

            _logger.LogInformation("Unidade criada automaticamente: {ClinicId}", clinicId);
        }

        return unit;
    }

    // Lista todas as unidades
    public async Task<List<Unit>> GetAllAsync()
    {
        return await _db.Units
            .OrderBy(u => u.Name)
            .ToListAsync();
    }

    /// <summary>
    /// Renomeia a unidade da clínica e PERSISTE a mudança (cria a unidade se não existir).
    /// </summary>
    public async Task<Unit> RenameAsync(int clinicId, string name)
    {
        var unit = await GetOrCreateAsync(clinicId);

        var trimmed = name?.Trim();
        if (!string.IsNullOrEmpty(trimmed) && trimmed != unit.Name)
        {
            unit.Name = trimmed;
            await _db.SaveChangesAsync();
            _logger.LogInformation("Unidade {ClinicId} renomeada para '{Name}'", clinicId, trimmed);
        }

        return unit;
    }

    public async Task<IEnumerable<LeadsPorUnidadeDto>> GetQuantityLeadsUnit(int clinicId)
    {
        var resultado = await _db.Leads
            .Where(l => l.TenantId == clinicId)
            .GroupBy(l => l.TenantId)
            .Select(g => new LeadsPorUnidadeDto
            {
                UnitId = g.Key,
                QuantidadeLeads = g.Count()
            })
            .ToListAsync();

        return resultado;
    }

    // ─────────────────────────────────────────────────────────────────────
    //  CRUD da tela de Unidades (multi-tenant + webhook por unidade)
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>Lista todas as unidades como DTO, com URL do webhook e contagem de leads.</summary>
    public async Task<List<UnitDto>> ListDtosAsync(string baseUrl, CancellationToken ct = default)
    {
        var units = await _db.Units.AsNoTracking().OrderBy(u => u.Name).ToListAsync(ct);

        // Contagem de leads por tenant numa única query.
        var counts = await _db.Leads
            .GroupBy(l => l.TenantId)
            .Select(g => new { TenantId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.TenantId, x => x.Count, ct);

        return units
            .Select(u => ToDto(u, baseUrl, counts.TryGetValue(u.ClinicId, out var c) ? c : 0))
            .ToList();
    }

    public async Task<UnitDto?> GetDtoByIdAsync(int id, string baseUrl, CancellationToken ct = default)
    {
        var unit = await _db.Units.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id, ct);
        if (unit is null) return null;

        var count = await _db.Leads.CountAsync(l => l.TenantId == unit.ClinicId, ct);
        return ToDto(unit, baseUrl, count);
    }

    /// <summary>Resolve a unidade pelo slug (case-insensitive). Usado pelo webhook da Kommo.</summary>
    public async Task<Unit?> ResolveBySlugAsync(string slug, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(slug)) return null;
        var normalized = slug.Trim().ToLowerInvariant();
        return await _db.Units.FirstOrDefaultAsync(u => u.Slug == normalized, ct);
    }

    public async Task<UnitDto> CreateAsync(CreateUnitDto dto, string baseUrl, CancellationToken ct = default)
    {
        var name = dto.Name.Trim();

        var slug = string.IsNullOrWhiteSpace(dto.Slug)
            ? await GenerateUniqueSlugAsync(name, ct)
            : await EnsureSlugUniqueAsync(Slugify(dto.Slug!), ct);

        var clinicId = dto.ClinicId ?? await NextClinicIdAsync(ct);

        if (await _db.Units.AnyAsync(u => u.ClinicId == clinicId, ct))
            throw new InvalidOperationException($"Já existe uma unidade com ClinicId {clinicId}.");

        var unit = new Unit
        {
            ClinicId = clinicId,
            Name = name,
            Slug = slug,
            Email = dto.Email?.Trim(),
            Cnpj = NormalizeCnpj(dto.Cnpj),
            Phone = dto.Phone?.Trim(),
            AddressLine = dto.AddressLine?.Trim(),
            AddressNumber = dto.AddressNumber?.Trim(),
            Neighborhood = dto.Neighborhood?.Trim(),
            City = dto.City?.Trim(),
            State = dto.State?.Trim().ToUpperInvariant(),
            ZipCode = NormalizeZipCode(dto.ZipCode),
            PhotoUrl = string.IsNullOrWhiteSpace(dto.PhotoUrl) ? DefaultPhotoUrl : dto.PhotoUrl!.Trim(),
            ResponsibleName = dto.ResponsibleName?.Trim(),
            KommoSubdomain = dto.KommoSubdomain?.Trim(),
            KommoAccountId = dto.KommoAccountId?.Trim(),
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
        };

        _db.Units.Add(unit);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Unidade criada: {Name} (ClinicId={ClinicId}, slug={Slug})", name, clinicId, slug);
        return ToDto(unit, baseUrl, 0);
    }

    public async Task<UnitDto?> UpdateAsync(int id, UpdateUnitDto dto, string baseUrl, CancellationToken ct = default)
    {
        var unit = await _db.Units.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (unit is null) return null;

        if (!string.IsNullOrWhiteSpace(dto.Name)) unit.Name = dto.Name.Trim();
        if (dto.Email is not null) unit.Email = dto.Email.Trim();
        if (dto.Cnpj is not null) unit.Cnpj = NormalizeCnpj(dto.Cnpj);
        if (dto.Phone is not null) unit.Phone = dto.Phone.Trim();
        if (dto.AddressLine is not null) unit.AddressLine = dto.AddressLine.Trim();
        if (dto.AddressNumber is not null) unit.AddressNumber = dto.AddressNumber.Trim();
        if (dto.Neighborhood is not null) unit.Neighborhood = dto.Neighborhood.Trim();
        if (dto.City is not null) unit.City = dto.City.Trim();
        if (dto.State is not null) unit.State = dto.State.Trim().ToUpperInvariant();
        if (dto.ZipCode is not null) unit.ZipCode = NormalizeZipCode(dto.ZipCode);
        if (dto.PhotoUrl is not null) unit.PhotoUrl = string.IsNullOrWhiteSpace(dto.PhotoUrl) ? DefaultPhotoUrl : dto.PhotoUrl.Trim();
        if (dto.ResponsibleName is not null) unit.ResponsibleName = dto.ResponsibleName.Trim();
        if (dto.KommoSubdomain is not null) unit.KommoSubdomain = dto.KommoSubdomain.Trim();
        if (dto.KommoAccountId is not null) unit.KommoAccountId = dto.KommoAccountId.Trim();
        if (dto.KommoStageMapJson is not null)
            unit.KommoStageMapJson = string.IsNullOrWhiteSpace(dto.KommoStageMapJson) ? null : dto.KommoStageMapJson.Trim();
        if (dto.IsActive.HasValue) unit.IsActive = dto.IsActive.Value;

        unit.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        var count = await _db.Leads.CountAsync(l => l.TenantId == unit.ClinicId, ct);
        return ToDto(unit, baseUrl, count);
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
    {
        var unit = await _db.Units.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (unit is null) return false;

        var hasLeads = await _db.Leads.AnyAsync(l => l.TenantId == unit.ClinicId, ct);
        if (hasLeads)
            throw new InvalidOperationException(
                "Esta unidade possui leads vinculados. Desative-a (IsActive=false) em vez de apagar.");

        _db.Units.Remove(unit);
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Unidade removida: {Id} (ClinicId={ClinicId})", id, unit.ClinicId);
        return true;
    }

    /// <summary>Monta a URL pública do webhook da Kommo para um slug.</summary>
    public static string? BuildWebhookUrl(string baseUrl, string? slug)
        => string.IsNullOrWhiteSpace(slug)
            ? null
            : $"{baseUrl.TrimEnd('/')}/webhooks/kommo/{slug}";

    public static UnitDto ToDto(Unit u, string baseUrl, int leadCount) => new()
    {
        Id = u.Id,
        ClinicId = u.ClinicId,
        Name = u.Name,
        Slug = u.Slug,
        Email = u.Email,
        Cnpj = u.Cnpj,
        Phone = u.Phone,
        AddressLine = u.AddressLine,
        AddressNumber = u.AddressNumber,
        Neighborhood = u.Neighborhood,
        City = u.City,
        State = u.State,
        ZipCode = u.ZipCode,
        PhotoUrl = u.PhotoUrl ?? DefaultPhotoUrl,
        ResponsibleName = u.ResponsibleName,
        IsActive = u.IsActive,
        KommoSubdomain = u.KommoSubdomain,
        KommoAccountId = u.KommoAccountId,
        KommoStageMapJson = u.KommoStageMapJson,
        WebhookUrl = BuildWebhookUrl(baseUrl, u.Slug),
        LeadCount = leadCount,
        CreatedAt = u.CreatedAt,
        UpdatedAt = u.UpdatedAt,
    };

    // ─── Helpers ─────────────────────────────────────────────────────────

    private async Task<int> NextClinicIdAsync(CancellationToken ct)
    {
        var max = await _db.Units.MaxAsync(u => (int?)u.ClinicId, ct) ?? 0;
        return max < 1000 ? 1000 : max + 1; // começa em 1000 pra não colidir com ids legados
    }

    private async Task<string> GenerateUniqueSlugAsync(string name, CancellationToken ct = default)
        => await EnsureSlugUniqueAsync(Slugify(name), ct);

    private async Task<string> EnsureSlugUniqueAsync(string baseSlug, CancellationToken ct)
    {
        var slug = string.IsNullOrWhiteSpace(baseSlug) ? "unidade" : baseSlug;
        var candidate = slug;
        var i = 2;
        while (await _db.Units.AnyAsync(u => u.Slug == candidate, ct))
            candidate = $"{slug}-{i++}";
        return candidate;
    }

    /// <summary>Converte "Unidade de Araguaína" → "unidade-de-araguaina".</summary>
    public static string Slugify(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        var normalized = text.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        foreach (var c in normalized)
        {
            var cat = CharUnicodeInfo.GetUnicodeCategory(c);
            if (cat == UnicodeCategory.NonSpacingMark) continue; // remove acentos
            if (char.IsLetterOrDigit(c)) sb.Append(c);
            else if (c is ' ' or '-' or '_' or '.') sb.Append('-');
        }

        var slug = sb.ToString();
        while (slug.Contains("--")) slug = slug.Replace("--", "-");
        return slug.Trim('-');
    }

    private static string? NormalizeCnpj(string? cnpj)
    {
        if (string.IsNullOrWhiteSpace(cnpj)) return null;
        var digits = new string(cnpj.Where(char.IsDigit).ToArray());
        return digits.Length == 0 ? null : digits;
    }

    private static string? NormalizeZipCode(string? zip)
    {
        if (string.IsNullOrWhiteSpace(zip)) return null;
        var digits = new string(zip.Where(char.IsDigit).ToArray());
        return digits.Length == 0 ? null : digits;
    }
}
