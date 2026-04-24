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

    public const int MaxPageSize = 200;
    public const int DefaultBatchSize = 500;
    public const int MaxBatchSize = 2000;
    public const int DefaultMaxBatchesPerCall = 4;
    public const int MaxBatchesPerCallCap = 20;

    public async Task<DuplicatesReportDto> FindDuplicatesAsync(
        int? tenantId,
        bool ignoreTenant,
        int page = 1,
        int pageSize = 50,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var cacheKey = BuildCacheKey(tenantId, ignoreTenant);

        List<DuplicateGroupDto>? allGroups = null;
        var cached = await _cache.GetStringAsync(cacheKey, ct);
        if (!string.IsNullOrEmpty(cached))
        {
            allGroups = JsonSerializer.Deserialize<List<DuplicateGroupDto>>(cached, JsonOpts);
            if (allGroups is not null)
                _logger.LogDebug("Duplicados cache HIT (key={Key})", cacheKey);
        }

        allGroups ??= await QueryGroupsAsync(tenantId, ignoreTenant, ct);

        if (cached is null or "")
        {
            await _cache.SetStringAsync(
                cacheKey,
                JsonSerializer.Serialize(allGroups, JsonOpts),
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = CacheTtl },
                ct);
        }

        var groupsFound = allGroups.Count;
        var contactsToDelete = allGroups.Sum(g => g.DeleteContactIds.Count);
        var totalPages = groupsFound == 0 ? 1 : (int)Math.Ceiling(groupsFound / (double)pageSize);

        var pageGroups = allGroups
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        _logger.LogInformation(
            "Duplicados: {Groups} grupo(s), {Delete} a apagar — página {Page}/{Total} (tenantId={TenantId}, ignoreTenant={Ignore})",
            groupsFound, contactsToDelete, page, totalPages, tenantId, ignoreTenant);

        return new DuplicatesReportDto
        {
            DryRun = true,
            GroupsFound = groupsFound,
            ContactsToDelete = contactsToDelete,
            Page = page,
            PageSize = pageSize,
            TotalPages = totalPages,
            Groups = pageGroups,
        };
    }

    public Task<(int GroupsFound, int ContactsToDelete)> GetDeleteEstimateAsync(
        int? tenantId, bool ignoreTenant, CancellationToken ct = default)
        => CountDuplicatesAsync(tenantId, ignoreTenant, ct);

    public async Task<int> DeleteOneBatchAsync(
        int? tenantId,
        bool ignoreTenant,
        int batchSize,
        CancellationToken ct = default)
    {
        batchSize = Math.Clamp(batchSize, 1, MaxBatchSize);
        const int BatchTimeoutSeconds = 60;

        var (sql, parameters) = BuildChunkedDeleteSql(tenantId, ignoreTenant, batchSize);
        var originalTimeout = _db.Database.GetCommandTimeout();
        _db.Database.SetCommandTimeout(BatchTimeoutSeconds);

        try
        {
            await using var tx = await _db.Database.BeginTransactionAsync(ct);
            var affected = await _db.Database.ExecuteSqlRawAsync(sql, parameters, ct);
            await tx.CommitAsync(ct);
            return affected;
        }
        finally
        {
            _db.Database.SetCommandTimeout(originalTimeout);
        }
    }

    public Task InvalidateReportCacheAsync(int? tenantId, CancellationToken ct = default)
        => InvalidateCacheAsync(tenantId, ct);

    public async Task<DuplicatesDeleteProgressDto> DeleteDuplicatesAsync(
        int? tenantId,
        bool ignoreTenant,
        int batchSize = DefaultBatchSize,
        int maxBatches = DefaultMaxBatchesPerCall,
        CancellationToken ct = default)
    {
        batchSize = Math.Clamp(batchSize, 1, MaxBatchSize);
        maxBatches = Math.Clamp(maxBatches, 1, MaxBatchesPerCallCap);

        const int BatchTimeoutSeconds = 60;
        var sw = System.Diagnostics.Stopwatch.StartNew();

        var (groupsFound, expectedToDelete) = await CountDuplicatesAsync(tenantId, ignoreTenant, ct);
        if (expectedToDelete == 0)
        {
            sw.Stop();
            await InvalidateCacheAsync(tenantId, ct);
            return new DuplicatesDeleteProgressDto
            {
                DeletedThisCall = 0,
                Batches = 0,
                Remaining = 0,
                ContactsToDeleteTotal = 0,
                GroupsFound = groupsFound,
                Completed = true,
                DurationMs = sw.ElapsedMilliseconds,
            };
        }

        var (sql, parameters) = BuildChunkedDeleteSql(tenantId, ignoreTenant, batchSize);

        var originalTimeout = _db.Database.GetCommandTimeout();
        _db.Database.SetCommandTimeout(BatchTimeoutSeconds);

        var deletedThisCall = 0;
        var batchesThisCall = 0;
        try
        {
            for (var i = 0; i < maxBatches; i++)
            {
                ct.ThrowIfCancellationRequested();

                await using var tx = await _db.Database.BeginTransactionAsync(ct);
                var affected = await _db.Database.ExecuteSqlRawAsync(sql, parameters, ct);
                await tx.CommitAsync(ct);

                if (affected <= 0) break;

                deletedThisCall += affected;
                batchesThisCall++;

                _logger.LogInformation(
                    "🗑 Lote {Batch} apagou {Affected} linha(s) nesta chamada (acum={Total})",
                    batchesThisCall, affected, deletedThisCall);

                if (affected < batchSize) break;
            }
        }
        finally
        {
            _db.Database.SetCommandTimeout(originalTimeout);
        }

        await InvalidateCacheAsync(tenantId, ct);

        var remaining = Math.Max(0, expectedToDelete - deletedThisCall);
        var completed = remaining == 0 || deletedThisCall == 0;

        sw.Stop();
        _logger.LogWarning(
            "🗑 DELETE call: {Deleted}/{Expected} nesta chamada em {Batches} lote(s), {Ms}ms (restante≈{Remaining}, completed={Completed})",
            deletedThisCall, expectedToDelete, batchesThisCall, sw.ElapsedMilliseconds, remaining, completed);

        return new DuplicatesDeleteProgressDto
        {
            DeletedThisCall = deletedThisCall,
            Batches = batchesThisCall,
            Remaining = remaining,
            ContactsToDeleteTotal = expectedToDelete,
            GroupsFound = groupsFound,
            Completed = completed,
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
        => $"duplicates:v2:t={tenantId?.ToString() ?? "all"}:it={(ignoreTenant ? 1 : 0)}";

    private async Task InvalidateCacheAsync(int? tenantId, CancellationToken ct)
    {
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
