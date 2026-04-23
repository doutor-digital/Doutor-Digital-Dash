using System.Data;
using System.Text.Json;
using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.DTOs.Admin;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;

namespace LeadAnalytics.Api.Service;

public class DuplicateContactService(
    AppDbContext db,
    IDistributedCache cache,
    ILogger<DuplicateContactService> logger)
{
    private readonly AppDbContext _db = db;
    private readonly IDistributedCache _cache = cache;
    private readonly ILogger<DuplicateContactService> _logger = logger;

    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public async Task<DuplicatesReportDto> FindDuplicatesAsync(
        int? tenantId,
        bool ignoreTenant,
        CancellationToken ct = default)
    {
        var cacheKey = BuildCacheKey(tenantId, ignoreTenant);

        var cached = await _cache.GetStringAsync(cacheKey, ct);
        if (!string.IsNullOrEmpty(cached))
        {
            var hit = JsonSerializer.Deserialize<DuplicatesReportDto>(cached, JsonOpts);
            if (hit is not null)
            {
                _logger.LogDebug("Duplicados cache HIT (key={Key})", cacheKey);
                return hit;
            }
        }

        var groups = await QueryGroupsAsync(tenantId, ignoreTenant, ct);

        var report = new DuplicatesReportDto
        {
            DryRun = true,
            GroupsFound = groups.Count,
            ContactsToDelete = groups.Sum(g => g.DeleteContactIds.Count),
            Groups = groups,
        };

        _logger.LogInformation(
            "Duplicados achados: {Groups} grupo(s), {Delete} a apagar (tenantId={TenantId}, ignoreTenant={Ignore})",
            report.GroupsFound, report.ContactsToDelete, tenantId, ignoreTenant);

        await _cache.SetStringAsync(
            cacheKey,
            JsonSerializer.Serialize(report, JsonOpts),
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = CacheTtl },
            ct);

        return report;
    }

    public async Task<DuplicatesReportDto> DeleteDuplicatesAsync(
        int? tenantId,
        bool ignoreTenant,
        CancellationToken ct = default)
    {
        var report = await FindDuplicatesAsync(tenantId, ignoreTenant, ct);
        if (report.ContactsToDelete == 0)
        {
            report.DryRun = false;
            return report;
        }

        var (sql, parameters) = BuildDeleteSql(tenantId, ignoreTenant);
        var deleted = await _db.Database.ExecuteSqlRawAsync(sql, parameters, ct);

        _logger.LogWarning(
            "🗑 Contacts duplicados apagados: {Deleted} linhas em {Groups} grupo(s) (tenantId={TenantId}, ignoreTenant={IgnoreTenant})",
            deleted, report.GroupsFound, tenantId, ignoreTenant);

        await InvalidateCacheAsync(tenantId, ct);

        report.DryRun = false;
        report.ContactsToDelete = deleted;
        return report;
    }

    // ─── SQL ─────────────────────────────────────────────────────────────

    private async Task<List<DuplicateGroupDto>> QueryGroupsAsync(
        int? tenantId, bool ignoreTenant, CancellationToken ct)
    {
        string sql;
        if (ignoreTenant)
        {
            // Cross-tenant: particiona só por telefone. O "keeper" é o mais antigo global.
            sql = @"
                WITH ranked AS (
                    SELECT ""Id"", ""Name"", ""PhoneNormalized"", ""CreatedAt"", ""TenantId"",
                           ROW_NUMBER() OVER (PARTITION BY ""PhoneNormalized"" ORDER BY ""CreatedAt"", ""Id"") AS rn,
                           COUNT(*)     OVER (PARTITION BY ""PhoneNormalized"") AS grp_count
                    FROM contacts
                )
                SELECT
                    MAX(CASE WHEN rn = 1 THEN ""TenantId""   END) AS tenant_id,
                    ""PhoneNormalized""                              AS phone,
                    grp_count                                        AS cnt,
                    MAX(CASE WHEN rn = 1 THEN ""Id""         END) AS keep_id,
                    MAX(CASE WHEN rn = 1 THEN ""Name""       END) AS keep_name,
                    MAX(CASE WHEN rn = 1 THEN ""CreatedAt""  END) AS keep_created_at,
                    COALESCE(ARRAY_AGG(""Id"" ORDER BY rn) FILTER (WHERE rn > 1), ARRAY[]::int[]) AS delete_ids
                FROM ranked
                WHERE grp_count > 1
                GROUP BY ""PhoneNormalized"", grp_count
                ORDER BY ""PhoneNormalized"";";
        }
        else
        {
            var tenantFilter = tenantId.HasValue ? "WHERE \"TenantId\" = @p0" : string.Empty;
            sql = $@"
                WITH ranked AS (
                    SELECT ""Id"", ""Name"", ""PhoneNormalized"", ""CreatedAt"", ""TenantId"",
                           ROW_NUMBER() OVER (PARTITION BY ""TenantId"", ""PhoneNormalized"" ORDER BY ""CreatedAt"", ""Id"") AS rn,
                           COUNT(*)     OVER (PARTITION BY ""TenantId"", ""PhoneNormalized"") AS grp_count
                    FROM contacts
                    {tenantFilter}
                )
                SELECT
                    ""TenantId""                                     AS tenant_id,
                    ""PhoneNormalized""                              AS phone,
                    grp_count                                        AS cnt,
                    MAX(CASE WHEN rn = 1 THEN ""Id""         END) AS keep_id,
                    MAX(CASE WHEN rn = 1 THEN ""Name""       END) AS keep_name,
                    MAX(CASE WHEN rn = 1 THEN ""CreatedAt""  END) AS keep_created_at,
                    COALESCE(ARRAY_AGG(""Id"" ORDER BY rn) FILTER (WHERE rn > 1), ARRAY[]::int[]) AS delete_ids
                FROM ranked
                WHERE grp_count > 1
                GROUP BY ""TenantId"", ""PhoneNormalized"", grp_count
                ORDER BY ""TenantId"", ""PhoneNormalized"";";
        }

        var conn = _db.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open)
            await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        if (!ignoreTenant && tenantId.HasValue)
        {
            var p = cmd.CreateParameter();
            p.ParameterName = "@p0";
            p.Value = tenantId.Value;
            cmd.Parameters.Add(p);
        }

        var result = new List<DuplicateGroupDto>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var rawCount = reader.GetValue(2);
            var count = rawCount is long l ? (int)l : Convert.ToInt32(rawCount);

            result.Add(new DuplicateGroupDto
            {
                TenantId = reader.GetInt32(0),
                PhoneNormalized = reader.GetString(1),
                Count = count,
                KeepContactId = reader.GetInt32(3),
                KeepName = reader.GetString(4),
                KeepCreatedAt = reader.GetDateTime(5),
                DeleteContactIds = ((int[])reader.GetValue(6)).ToList(),
            });
        }

        return result;
    }

    private static (string Sql, object[] Params) BuildDeleteSql(int? tenantId, bool ignoreTenant)
    {
        if (ignoreTenant)
        {
            const string sql = @"
                DELETE FROM contacts
                WHERE ""Id"" IN (
                    SELECT ""Id"" FROM (
                        SELECT ""Id"",
                               ROW_NUMBER() OVER (PARTITION BY ""PhoneNormalized"" ORDER BY ""CreatedAt"", ""Id"") AS rn
                        FROM contacts
                    ) t
                    WHERE t.rn > 1
                );";
            return (sql, Array.Empty<object>());
        }

        if (tenantId.HasValue)
        {
            const string sql = @"
                DELETE FROM contacts
                WHERE ""Id"" IN (
                    SELECT ""Id"" FROM (
                        SELECT ""Id"",
                               ROW_NUMBER() OVER (PARTITION BY ""TenantId"", ""PhoneNormalized"" ORDER BY ""CreatedAt"", ""Id"") AS rn
                        FROM contacts
                        WHERE ""TenantId"" = {0}
                    ) t
                    WHERE t.rn > 1
                );";
            return (sql, new object[] { tenantId.Value });
        }

        const string sqlAllTenants = @"
            DELETE FROM contacts
            WHERE ""Id"" IN (
                SELECT ""Id"" FROM (
                    SELECT ""Id"",
                           ROW_NUMBER() OVER (PARTITION BY ""TenantId"", ""PhoneNormalized"" ORDER BY ""CreatedAt"", ""Id"") AS rn
                    FROM contacts
                ) t
                WHERE t.rn > 1
            );";
        return (sqlAllTenants, Array.Empty<object>());
    }

    // ─── Cache helpers ───────────────────────────────────────────────────

    private static string BuildCacheKey(int? tenantId, bool ignoreTenant)
        => $"duplicates:v1:t={tenantId?.ToString() ?? "all"}:it={(ignoreTenant ? 1 : 0)}";

    private async Task InvalidateCacheAsync(int? tenantId, CancellationToken ct)
    {
        // Invalida todas as variações potencialmente afetadas: o tenant específico (se houver),
        // o modo "all tenants" e o modo cross-tenant (ignoreTenant=true).
        var keys = new List<string>
        {
            BuildCacheKey(null, false),
            BuildCacheKey(null, true),
        };
        if (tenantId.HasValue)
            keys.Add(BuildCacheKey(tenantId.Value, false));

        foreach (var key in keys)
            await _cache.RemoveAsync(key, ct);
    }
}
