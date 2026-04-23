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

    public async Task<DuplicatesDeleteSummaryDto> DeleteDuplicatesAsync(
        int? tenantId,
        bool ignoreTenant,
        CancellationToken ct = default)
    {
        const int BatchSize = 1000;
        const int BatchTimeoutSeconds = 120;

        var sw = System.Diagnostics.Stopwatch.StartNew();

        var (groupsFound, expectedToDelete) = await CountDuplicatesAsync(tenantId, ignoreTenant, ct);
        if (expectedToDelete == 0)
        {
            sw.Stop();
            return new DuplicatesDeleteSummaryDto
            {
                GroupsFound = 0,
                ContactsDeleted = 0,
                Batches = 0,
                DurationMs = sw.ElapsedMilliseconds,
            };
        }

        var (sql, parameters) = BuildChunkedDeleteSql(tenantId, ignoreTenant, BatchSize);

        var originalTimeout = _db.Database.GetCommandTimeout();
        _db.Database.SetCommandTimeout(BatchTimeoutSeconds);

        var totalDeleted = 0;
        var batches = 0;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var affected = await _db.Database.ExecuteSqlRawAsync(sql, parameters, ct);
                if (affected <= 0) break;

                totalDeleted += affected;
                batches++;

                _logger.LogInformation(
                    "🗑 Lote {Batch} apagou {Affected} linha(s) (total={Total}/{Expected})",
                    batches, affected, totalDeleted, expectedToDelete);

                if (affected < BatchSize) break;
            }
        }
        finally
        {
            _db.Database.SetCommandTimeout(originalTimeout);
        }

        await InvalidateCacheAsync(tenantId, ct);

        sw.Stop();

        _logger.LogWarning(
            "🗑 Duplicados apagados: {Deleted} linha(s) em {Batches} lote(s), {Groups} grupo(s), {Ms}ms (tenantId={TenantId}, ignoreTenant={IgnoreTenant})",
            totalDeleted, batches, groupsFound, sw.ElapsedMilliseconds, tenantId, ignoreTenant);

        return new DuplicatesDeleteSummaryDto
        {
            GroupsFound = groupsFound,
            ContactsDeleted = totalDeleted,
            Batches = batches,
            DurationMs = sw.ElapsedMilliseconds,
        };
    }

    private async Task<(int GroupsFound, int ContactsToDelete)> CountDuplicatesAsync(
        int? tenantId, bool ignoreTenant, CancellationToken ct)
    {
        string sql;
        if (ignoreTenant)
        {
            sql = @"
                WITH grp AS (
                    SELECT COUNT(*) AS cnt
                    FROM contacts
                    GROUP BY ""PhoneNormalized""
                    HAVING COUNT(*) > 1
                )
                SELECT COUNT(*)::int AS groups_found,
                       COALESCE(SUM(cnt - 1), 0)::int AS to_delete
                FROM grp;";
        }
        else
        {
            var tenantFilter = tenantId.HasValue ? "WHERE \"TenantId\" = @p0" : string.Empty;
            sql = $@"
                WITH grp AS (
                    SELECT COUNT(*) AS cnt
                    FROM contacts
                    {tenantFilter}
                    GROUP BY ""TenantId"", ""PhoneNormalized""
                    HAVING COUNT(*) > 1
                )
                SELECT COUNT(*)::int AS groups_found,
                       COALESCE(SUM(cnt - 1), 0)::int AS to_delete
                FROM grp;";
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

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return (0, 0);

        return (reader.GetInt32(0), reader.GetInt32(1));
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

    private static (string Sql, object[] Params) BuildChunkedDeleteSql(int? tenantId, bool ignoreTenant, int batchSize)
    {
        if (ignoreTenant)
        {
            var sql = $@"
                DELETE FROM contacts
                WHERE ""Id"" IN (
                    SELECT ""Id"" FROM (
                        SELECT ""Id"",
                               ROW_NUMBER() OVER (PARTITION BY ""PhoneNormalized"" ORDER BY ""CreatedAt"", ""Id"") AS rn
                        FROM contacts
                    ) t
                    WHERE t.rn > 1
                    LIMIT {batchSize}
                );";
            return (sql, Array.Empty<object>());
        }

        if (tenantId.HasValue)
        {
            var sql = $@"
                DELETE FROM contacts
                WHERE ""Id"" IN (
                    SELECT ""Id"" FROM (
                        SELECT ""Id"",
                               ROW_NUMBER() OVER (PARTITION BY ""TenantId"", ""PhoneNormalized"" ORDER BY ""CreatedAt"", ""Id"") AS rn
                        FROM contacts
                        WHERE ""TenantId"" = {{0}}
                    ) t
                    WHERE t.rn > 1
                    LIMIT {batchSize}
                );";
            return (sql, new object[] { tenantId.Value });
        }

        var sqlAllTenants = $@"
            DELETE FROM contacts
            WHERE ""Id"" IN (
                SELECT ""Id"" FROM (
                    SELECT ""Id"",
                           ROW_NUMBER() OVER (PARTITION BY ""TenantId"", ""PhoneNormalized"" ORDER BY ""CreatedAt"", ""Id"") AS rn
                    FROM contacts
                ) t
                WHERE t.rn > 1
                LIMIT {batchSize}
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
