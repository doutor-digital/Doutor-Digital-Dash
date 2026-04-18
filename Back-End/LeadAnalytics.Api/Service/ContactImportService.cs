using System.Globalization;
using System.Text;
using System.Text.Json;
using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.DTOs.Response;
using LeadAnalytics.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace LeadAnalytics.Api.Service;

public class ContactImportService(
    AppDbContext db,
    ILogger<ContactImportService> logger)
{
    private readonly AppDbContext _db = db;
    private readonly ILogger<ContactImportService> _logger = logger;

    private static readonly TimeZoneInfo BrTz =
        TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");

    private const int BatchSize = 500;
    private const int MaxErrorSamples = 50;

    public async Task<ContactImportResultDto> ImportCsvAsync(
        int tenantId,
        string filename,
        Stream csvStream,
        string onDuplicate = "skip",
        int? uploadedByUserId = null,
        CancellationToken ct = default)
    {
        var batch = new ImportBatch
        {
            TenantId = tenantId,
            Filename = filename,
            UploadedByUserId = uploadedByUserId,
            Status = "processing",
            CreatedAt = DateTime.UtcNow,
        };
        _db.ImportBatches.Add(batch);
        await _db.SaveChangesAsync(ct);

        var result = new ContactImportResultDto
        {
            BatchId = batch.Id,
            Filename = filename,
        };

        var errors = new List<ContactImportErrorDto>();
        var buffer = new List<Contact>(BatchSize);
        var seenInFile = new HashSet<string>();

        using var reader = new StreamReader(csvStream, Encoding.UTF8);
        string? headerLine = await reader.ReadLineAsync(ct);
        if (headerLine is null)
        {
            batch.Status = "done";
            batch.FinishedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            return result;
        }

        headerLine = StripBom(headerLine);

        int rowNum = 1;
        string? line;
        while ((line = await reader.ReadLineAsync(ct)) is not null)
        {
            rowNum++;
            if (string.IsNullOrWhiteSpace(line)) continue;

            var fields = ParseCsvLine(line);
            if (fields.Count < 12)
            {
                AddError(errors, rowNum, "linha malformada", line);
                result.Errors++;
                continue;
            }

            var name = fields[0].Trim();
            var phoneRaw = fields[1].Trim();

            if (string.IsNullOrWhiteSpace(name))
            {
                AddError(errors, rowNum, "nome vazio", phoneRaw);
                result.Errors++;
                continue;
            }

            var phone = NormalizePhone(phoneRaw);
            if (phone is null)
            {
                AddError(errors, rowNum, "telefone inválido", phoneRaw);
                result.Errors++;
                continue;
            }

            // Dedup dentro do próprio arquivo
            if (!seenInFile.Add(phone))
            {
                result.Skipped++;
                continue;
            }

            var tags = SplitTags(fields[4]);
            var adIds = SplitAdIds(fields[9]);

            var contact = new Contact
            {
                TenantId = tenantId,
                Name = name,
                PhoneNormalized = phone,
                PhoneRaw = phoneRaw,
                Origem = "import_csv",
                ImportBatchId = batch.Id,
                ImportedAt = DateTime.UtcNow,
                Conexao = Null(fields[2]),
                Observacoes = Null(fields[3]),
                TagsJson = tags.Count > 0 ? JsonSerializer.Serialize(tags) : null,
                Etapa = Null(fields[5]),
                ConsultationAt = ParseBrDateTime(fields[6]),
                ConsultationRegisteredAt = ParseBrDateTime(fields[7]),
                LastMessageAt = ParseBrDateTime(fields[8]),
                MetaAdsIdsJson = adIds.Count > 0 ? JsonSerializer.Serialize(adIds) : null,
                Birthday = ParseBrDate(fields[10]),
                Blocked = string.Equals(fields[11].Trim(), "Sim", StringComparison.OrdinalIgnoreCase),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            };

            buffer.Add(contact);

            if (buffer.Count >= BatchSize)
            {
                await FlushAsync(buffer, tenantId, onDuplicate, batch.Id, result, errors, ct);
                buffer.Clear();
            }
        }

        if (buffer.Count > 0)
        {
            await FlushAsync(buffer, tenantId, onDuplicate, batch.Id, result, errors, ct);
            buffer.Clear();
        }

        result.TotalRows = rowNum - 1;
        result.ErrorSamples = errors.Take(MaxErrorSamples).ToList();

        batch.TotalRows = result.TotalRows;
        batch.CreatedCount = result.Created;
        batch.UpdatedCount = result.Updated;
        batch.SkippedCount = result.Skipped;
        batch.ErrorCount = result.Errors;
        batch.ErrorsJson = JsonSerializer.Serialize(result.ErrorSamples);
        batch.Status = "done";
        batch.FinishedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "📥 Import CSV finalizado. Tenant={Tenant} Batch={Batch} Criados={C} Ignorados={S} Erros={E}",
            tenantId, batch.Id, result.Created, result.Skipped, result.Errors);

        return result;
    }

    private async Task FlushAsync(
        List<Contact> buffer,
        int tenantId,
        string onDuplicate,
        int batchId,
        ContactImportResultDto result,
        List<ContactImportErrorDto> errors,
        CancellationToken ct)
    {
        var phones = buffer.Select(c => c.PhoneNormalized).ToList();

        var existing = await _db.Contacts
            .Where(c => c.TenantId == tenantId && phones.Contains(c.PhoneNormalized))
            .ToDictionaryAsync(c => c.PhoneNormalized, ct);

        foreach (var contact in buffer)
        {
            if (existing.TryGetValue(contact.PhoneNormalized, out var current))
            {
                if (onDuplicate == "skip")
                {
                    result.Skipped++;
                    continue;
                }

                if (onDuplicate == "update")
                {
                    MergeInto(current, contact, batchId);
                    result.Updated++;
                    continue;
                }

                if (onDuplicate == "fail")
                {
                    throw new InvalidOperationException(
                        $"Contato duplicado (telefone {contact.PhoneNormalized})");
                }
            }
            else
            {
                _db.Contacts.Add(contact);
                result.Created++;
            }
        }

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Erro ao persistir lote de contatos");
            errors.Add(new ContactImportErrorDto
            {
                Row = 0,
                Reason = "erro ao persistir lote: " + (ex.InnerException?.Message ?? ex.Message)
            });
            result.Errors++;
            _db.ChangeTracker.Clear();
        }
    }

    private static void MergeInto(Contact current, Contact incoming, int batchId)
    {
        // Se veio do webhook da Cloudia, preservar a origem e só marcar tag extra
        if (current.Origem == "webhook_cloudia")
        {
            var existingTags = DeserializeTags(current.TagsJson);
            existingTags.Add($"reimportado_via_csv_{DateTime.UtcNow:dd/MM/yyyy}");
            current.TagsJson = JsonSerializer.Serialize(existingTags);
        }
        else
        {
            current.ImportBatchId = batchId;
            current.ImportedAt = DateTime.UtcNow;
        }

        // Merge — só atualiza se vier valor
        if (!string.IsNullOrWhiteSpace(incoming.Name)) current.Name = incoming.Name;
        if (!string.IsNullOrWhiteSpace(incoming.PhoneRaw)) current.PhoneRaw = incoming.PhoneRaw;
        if (!string.IsNullOrWhiteSpace(incoming.Conexao)) current.Conexao = incoming.Conexao;
        if (!string.IsNullOrWhiteSpace(incoming.Observacoes)) current.Observacoes = incoming.Observacoes;
        if (!string.IsNullOrWhiteSpace(incoming.Etapa)) current.Etapa = incoming.Etapa;
        if (!string.IsNullOrWhiteSpace(incoming.TagsJson) && current.Origem != "webhook_cloudia")
            current.TagsJson = incoming.TagsJson;
        if (!string.IsNullOrWhiteSpace(incoming.MetaAdsIdsJson)) current.MetaAdsIdsJson = incoming.MetaAdsIdsJson;
        if (incoming.ConsultationAt.HasValue) current.ConsultationAt = incoming.ConsultationAt;
        if (incoming.ConsultationRegisteredAt.HasValue) current.ConsultationRegisteredAt = incoming.ConsultationRegisteredAt;
        if (incoming.LastMessageAt.HasValue) current.LastMessageAt = incoming.LastMessageAt;
        if (incoming.Birthday.HasValue) current.Birthday = incoming.Birthday;
        if (incoming.Blocked) current.Blocked = true;

        current.UpdatedAt = DateTime.UtcNow;
    }

    private static List<string> DeserializeTags(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? new();
        }
        catch
        {
            return new() { json };
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  PARSER / NORMALIZAÇÃO
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Parser de linha CSV com ; como separador e " como delimitador de string.
    /// Suporta aspas escapadas ("").
    /// </summary>
    public static List<string> ParseCsvLine(string line)
    {
        var result = new List<string>();
        var sb = new StringBuilder();
        var inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            var c = line[i];

            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    {
                        sb.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    sb.Append(c);
                }
            }
            else
            {
                if (c == '"')
                {
                    inQuotes = true;
                }
                else if (c == ';')
                {
                    result.Add(sb.ToString());
                    sb.Clear();
                }
                else
                {
                    sb.Append(c);
                }
            }
        }

        result.Add(sb.ToString());
        return result;
    }

    /// <summary>
    /// Normaliza telefone BR: remove máscara, garante DDI 55, valida tamanho 12/13.
    /// Retorna null se inválido.
    /// </summary>
    public static string? NormalizePhone(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        var digits = new StringBuilder(raw.Length);
        foreach (var c in raw)
            if (char.IsDigit(c)) digits.Append(c);

        var phone = digits.ToString();
        if (phone.Length == 0) return null;

        if (!(phone.StartsWith("55") && phone.Length >= 12))
            phone = "55" + phone;

        if (phone.Length < 12 || phone.Length > 13) return null;
        return phone;
    }

    private static List<string> SplitTags(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return new();
        return raw
            .Split(new[] { "|" }, StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Trim())
            .Where(t => t.Length > 0)
            .ToList();
    }

    private static List<string> SplitAdIds(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return new();
        return raw
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Trim())
            .Where(t => t.Length > 0)
            .ToList();
    }

    private static DateTime? ParseBrDateTime(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        if (DateTime.TryParseExact(
                raw.Trim(),
                new[] { "dd/MM/yyyy HH:mm", "dd/MM/yyyy HH:mm:ss", "dd/MM/yyyy" },
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeLocal,
                out var local))
        {
            return TimeZoneInfo.ConvertTimeToUtc(
                DateTime.SpecifyKind(local, DateTimeKind.Unspecified),
                BrTz);
        }
        return null;
    }

    private static DateTime? ParseBrDate(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        if (DateTime.TryParseExact(
                raw.Trim(),
                "dd/MM/yyyy",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var dt))
        {
            return DateTime.SpecifyKind(dt, DateTimeKind.Utc);
        }
        return null;
    }

    private static string? Null(string raw) =>
        string.IsNullOrWhiteSpace(raw) ? null : raw.Trim();

    private static string StripBom(string s) =>
        s.Length > 0 && s[0] == '\uFEFF' ? s[1..] : s;

    private static void AddError(List<ContactImportErrorDto> errors, int row, string reason, string? value)
    {
        if (errors.Count < MaxErrorSamples)
        {
            errors.Add(new ContactImportErrorDto { Row = row, Reason = reason, Value = value });
        }
    }
}
