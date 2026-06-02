using System.Data;
using System.Text.Json;
using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.DTOs.Admin;
using Microsoft.EntityFrameworkCore;

namespace LeadAnalytics.Api.Service;

/// <summary>
/// Deduplicação de LEADS por (TenantId, telefone normalizado). Em cada grupo o lead
/// "mais avançado" (pagamento → agendamento → maior valor → etapa → mais antigo) é
/// MANTIDO; os demais são apagados.
///
/// Apagar é destrutivo: a API da Kommo não permite excluir leads, então (quando
/// solicitado) marcamos os duplicados com a tag "DUPLICADO" na Kommo (via PATCH) e
/// apagamos do nosso banco. A exclusão no banco limpa antes as tabelas filhas com FK
/// RESTRICT (assignments, stage_histories, conversations, payments); as demais são
/// cascade/set-null no próprio banco.
/// </summary>
public class DuplicateLeadService(
    AppDbContext db,
    KommoApiClient kommo,
    ILogger<DuplicateLeadService> logger)
{
    private readonly AppDbContext _db = db;
    private readonly KommoApiClient _kommo = kommo;
    private readonly ILogger<DuplicateLeadService> _logger = logger;

    public const int MaxPageSize = 200;
    public const int DefaultBatchSize = 200;
    public const int MaxBatchSize = 1000;
    public const string DuplicateTag = "DUPLICADO";

    private const int MinPhoneDigits = 8;

    // Telefone só com dígitos.
    private const string DigitsExpr = "regexp_replace(\"Phone\", '[^0-9]', '', 'g')";

    // Forma canônica do telefone: tira o código do país "55" quando o número tem
    // 12-13 dígitos (caso clássico em que a Kommo grava uns com 55 e outros sem).
    // Assim "5599999999999" e "99999999999" agrupam como o MESMO telefone.
    private const string PhoneExpr =
        "(CASE WHEN length(" + DigitsExpr + ") IN (12, 13) AND left(" + DigitsExpr + ", 2) = '55' " +
        "THEN right(" + DigitsExpr + ", length(" + DigitsExpr + ") - 2) " +
        "ELSE " + DigitsExpr + " END)";

    // Nome canônico p/ o modo "nome": minúsculo, sem espaços duplicados, aparado.
    private const string NameExpr = "lower(btrim(regexp_replace(\"Name\", '\\s+', ' ', 'g')))";

    public const string ModePhone = "phone";
    public const string ModeName = "name";

    // rn=1 => lead mantido (o mais avançado). rn>1 => apagar.
    private const string RankOrder =
        "\"HasPayment\" DESC, \"HasAppointment\" DESC, COALESCE(\"Price\",0) DESC, " +
        "COALESCE(\"CurrentStageId\",-1) DESC, \"CreatedAt\" ASC, \"Id\" ASC";

    public readonly record struct BatchResult(int Deleted, int Tagged, int TagFailed, int TagSkipped, int TagConfirmed);

    // ─── Relatório (dry-run) ────────────────────────────────────────────────

    public async Task<LeadDuplicatesReportDto> FindDuplicatesAsync(
        int? tenantId, bool ignoreTenant, string mode, int page = 1, int pageSize = 50, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);
        mode = Normalize(mode);

        var all = await QueryGroupsAsync(tenantId, ignoreTenant, mode, ct);
        var scanned = await CountScannedAsync(tenantId, ignoreTenant, ct);
        var groupsFound = all.Count;
        var leadsToDelete = all.Sum(g => g.DeleteLeadIds.Count);
        var totalPages = groupsFound == 0 ? 1 : (int)Math.Ceiling(groupsFound / (double)pageSize);

        return new LeadDuplicatesReportDto
        {
            DryRun = true,
            Mode = mode,
            LeadsScanned = scanned,
            GroupsFound = groupsFound,
            LeadsToDelete = leadsToDelete,
            Page = page,
            PageSize = pageSize,
            TotalPages = totalPages,
            Groups = all.Skip((page - 1) * pageSize).Take(pageSize).ToList(),
        };
    }

    public Task<(int GroupsFound, int LeadsToDelete)> GetDeleteEstimateAsync(
        int? tenantId, bool ignoreTenant, string mode, CancellationToken ct = default)
        => CountDuplicatesAsync(tenantId, ignoreTenant, Normalize(mode), ct);

    private static string Normalize(string? mode)
        => string.Equals(mode, ModeName, StringComparison.OrdinalIgnoreCase) ? ModeName : ModePhone;

    // ─── Exclusão de um lote ────────────────────────────────────────────────

    public async Task<BatchResult> DeleteOneBatchAsync(
        int? tenantId, bool ignoreTenant, int batchSize, bool tagInKommo, string mode, CancellationToken ct = default)
    {
        batchSize = Math.Clamp(batchSize, 1, MaxBatchSize);
        mode = Normalize(mode);

        var rows = await SelectDeleteBatchAsync(tenantId, ignoreTenant, mode, batchSize, ct);
        if (rows.Count == 0) return new BatchResult(0, 0, 0, 0, 0);

        var ids = rows.Select(r => r.Id).ToArray();

        var tagged = 0;
        var tagFailed = 0;
        var tagSkipped = 0;
        var tagConfirmed = 0;
        if (tagInKommo)
            (tagged, tagFailed, tagSkipped, tagConfirmed) = await TagInKommoAsync(rows, ct);

        var deleted = await DeleteLeadsCascadeAsync(ids, ct);

        _logger.LogInformation(
            "🗑 Lote leads duplicados: apagados={Deleted} tagueados={Tagged} confirmados={Confirmed} falhasTag={Failed} puladosSemToken={Skipped}",
            deleted, tagged, tagConfirmed, tagFailed, tagSkipped);

        return new BatchResult(deleted, tagged, tagFailed, tagSkipped, tagConfirmed);
    }

    // ─── Tagueia "DUPLICADO" na Kommo (best-effort, por unidade) ─────────────
    // Retorna (tagueados enviados, falhas HTTP, pulados sem token, confirmados via re-GET).

    private async Task<(int Tagged, int Failed, int Skipped, int Confirmed)> TagInKommoAsync(List<DeleteRow> rows, CancellationToken ct)
    {
        // Resolve unidades envolvidas (por UnitId e por ClinicId=TenantId como fallback).
        var unitIds = rows.Where(r => r.UnitId.HasValue).Select(r => r.UnitId!.Value).Distinct().ToList();
        var tenantIds = rows.Select(r => r.TenantId).Distinct().ToList();

        var units = await _db.Units.AsNoTracking()
            .Where(u => unitIds.Contains(u.Id) || tenantIds.Contains(u.ClinicId))
            .Select(u => new { u.Id, u.ClinicId, u.KommoSubdomain, u.KommoAccessToken })
            .ToListAsync(ct);

        var byUnit = units.Where(u => u.Id != 0).ToDictionary(u => u.Id);
        var byClinic = units.GroupBy(u => u.ClinicId).ToDictionary(g => g.Key, g => g.First());

        // Agrupa leads por unidade resolvida (que tenha subdomínio + token).
        var perUnit = new Dictionary<int, (string Sub, string Token, List<(long Id, List<string> Tags)> Items)>();
        var skipped = 0;

        foreach (var r in rows)
        {
            var u = (r.UnitId.HasValue && byUnit.TryGetValue(r.UnitId.Value, out var bu)) ? bu
                  : (byClinic.TryGetValue(r.TenantId, out var bc) ? bc : null);

            if (u is null || r.ExternalId <= 0 ||
                string.IsNullOrWhiteSpace(u.KommoSubdomain) || string.IsNullOrWhiteSpace(u.KommoAccessToken))
            {
                // Sem token/subdomínio na unidade ou sem id da Kommo → não dá pra taguear.
                skipped++;
                continue;
            }

            var tags = ParseTags(r.TagsJson);
            if (!tags.Contains(DuplicateTag, StringComparer.OrdinalIgnoreCase))
                tags.Add(DuplicateTag);

            if (!perUnit.TryGetValue(u.Id, out var bucket))
            {
                bucket = (u.KommoSubdomain!, u.KommoAccessToken!, new List<(long, List<string>)>());
                perUnit[u.Id] = bucket;
            }
            bucket.Items.Add(((long)r.ExternalId, tags));
        }

        var tagged = 0;
        var failed = 0;
        var confirmed = 0;
        foreach (var (_, bucket) in perUnit)
        {
            var ids = bucket.Items.Select(i => i.Id).ToList();

            try
            {
                tagged += await _kommo.PatchLeadsTagsAsync(bucket.Sub, bucket.Token, bucket.Items, ct);
            }
            catch (Exception ex)
            {
                failed += bucket.Items.Count;
                _logger.LogWarning(ex, "Falha ao taguear {Count} lead(s) duplicados na Kommo (sub={Sub})",
                    bucket.Items.Count, bucket.Sub);
                continue;
            }

            // Confirma que a tag realmente entrou (re-GET por ID).
            try
            {
                var confirmedSet = await _kommo.GetLeadIdsWithTagAsync(bucket.Sub, bucket.Token, ids, DuplicateTag, ct);
                confirmed += confirmedSet.Count;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Falha ao confirmar tag na Kommo (sub={Sub}) — PATCH ok, verificação falhou", bucket.Sub);
            }
        }

        return (tagged, failed, skipped, confirmed);
    }

    private static List<string> ParseTags(string? tagsJson)
    {
        if (string.IsNullOrWhiteSpace(tagsJson)) return [];
        try
        {
            var list = JsonSerializer.Deserialize<List<string>>(tagsJson);
            return list?.Where(s => !string.IsNullOrWhiteSpace(s)).ToList() ?? [];
        }
        catch
        {
            return [];
        }
    }

    // ─── Exclusão no banco (transação + limpeza de FKs RESTRICT) ─────────────

    private async Task<int> DeleteLeadsCascadeAsync(int[] ids, CancellationToken ct)
    {
        const int TimeoutSeconds = 90;
        var original = _db.Database.GetCommandTimeout();
        _db.Database.SetCommandTimeout(TimeoutSeconds);

        try
        {
            await using var tx = await _db.Database.BeginTransactionAsync(ct);

            // FKs RESTRICT precisam ser limpas antes do lead.
            await _db.Database.ExecuteSqlRawAsync(
                "DELETE FROM lead_assignments WHERE \"LeadId\" = ANY({0})", new object[] { ids }, ct);
            await _db.Database.ExecuteSqlRawAsync(
                "DELETE FROM lead_stage_histories WHERE \"LeadId\" = ANY({0})", new object[] { ids }, ct);
            // payment_splits faz cascade a partir de payments.
            await _db.Database.ExecuteSqlRawAsync(
                "DELETE FROM payments WHERE \"LeadId\" = ANY({0})", new object[] { ids }, ct);
            // lead_interactions faz cascade a partir de lead_conversations.
            await _db.Database.ExecuteSqlRawAsync(
                "DELETE FROM lead_conversations WHERE \"LeadId\" = ANY({0})", new object[] { ids }, ct);

            // Cascade/set-null cuidam do resto (recovery_attempts, lead_payment_receipts,
            // consultations→treatments, agent_conversations).
            var deleted = await _db.Database.ExecuteSqlRawAsync(
                "DELETE FROM leads WHERE \"Id\" = ANY({0})", new object[] { ids }, ct);

            await tx.CommitAsync(ct);
            return deleted;
        }
        finally
        {
            _db.Database.SetCommandTimeout(original);
        }
    }

    // ─── Queries (ADO) ───────────────────────────────────────────────────────

    private readonly record struct DeleteRow(int Id, int ExternalId, int? UnitId, int TenantId, string? TagsJson);

    private async Task<List<DeleteRow>> SelectDeleteBatchAsync(
        int? tenantId, bool ignoreTenant, string mode, int batchSize, CancellationToken ct)
    {
        var (partition, filter, useParam, _) = BuildPartitionFilter(tenantId, ignoreTenant, mode);

        var sql = $@"
            SELECT ""Id"", ""ExternalId"", ""UnitId"", ""TenantId"", ""TagsJson"" FROM (
                SELECT ""Id"", ""ExternalId"", ""UnitId"", ""TenantId"", ""TagsJson"",
                       ROW_NUMBER() OVER (PARTITION BY {partition} ORDER BY {RankOrder}) AS rn
                FROM leads
                {filter}
            ) t
            WHERE t.rn > 1
            LIMIT {batchSize};";

        var conn = _db.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open) await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        if (useParam) AddTenantParam(cmd, tenantId!.Value);

        var result = new List<DeleteRow>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            result.Add(new DeleteRow(
                Id: reader.GetInt32(0),
                ExternalId: reader.IsDBNull(1) ? 0 : reader.GetInt32(1),
                UnitId: reader.IsDBNull(2) ? null : reader.GetInt32(2),
                TenantId: reader.GetInt32(3),
                TagsJson: reader.IsDBNull(4) ? null : reader.GetString(4)));
        }
        return result;
    }

    private async Task<(int GroupsFound, int LeadsToDelete)> CountDuplicatesAsync(
        int? tenantId, bool ignoreTenant, string mode, CancellationToken ct)
    {
        var (partition, filter, useParam, _) = BuildPartitionFilter(tenantId, ignoreTenant, mode);

        var sql = $@"
            WITH grp AS (
                SELECT COUNT(*) AS cnt
                FROM leads
                {filter}
                GROUP BY {partition}
                HAVING COUNT(*) > 1
            )
            SELECT COUNT(*)::int AS groups_found,
                   COALESCE(SUM(cnt - 1), 0)::int AS to_delete
            FROM grp;";

        var conn = _db.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open) await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        if (useParam) AddTenantParam(cmd, tenantId!.Value);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return (0, 0);
        return (reader.GetInt32(0), reader.GetInt32(1));
    }

    /// <summary>Quantos leads (do tenant) entram na análise — com chave válida no modo escolhido.</summary>
    private async Task<int> CountScannedAsync(int? tenantId, bool ignoreTenant, CancellationToken ct)
    {
        var q = _db.Leads.AsNoTracking().AsQueryable();
        if (!ignoreTenant && tenantId.HasValue) q = q.Where(l => l.TenantId == tenantId.Value);
        return await q.CountAsync(ct);
    }

    private async Task<List<LeadDuplicateGroupDto>> QueryGroupsAsync(
        int? tenantId, bool ignoreTenant, string mode, CancellationToken ct)
    {
        var (partition, filter, useParam, keyExpr) = BuildPartitionFilter(tenantId, ignoreTenant, mode);

        var sql = $@"
            WITH ranked AS (
                SELECT ""Id"", ""Name"", ""CurrentStage"", ""HasPayment"", ""HasAppointment"",
                       ""Price"", ""CreatedAt"", ""TenantId"",
                       {keyExpr} AS phone,
                       ROW_NUMBER() OVER (PARTITION BY {partition} ORDER BY {RankOrder}) AS rn,
                       COUNT(*)     OVER (PARTITION BY {partition}) AS grp_count
                FROM leads
                {filter}
            )
            SELECT
                MAX(CASE WHEN rn = 1 THEN ""TenantId"" END)        AS tenant_id,
                phone,
                grp_count                                           AS cnt,
                MAX(CASE WHEN rn = 1 THEN ""Id"" END)              AS keep_id,
                MAX(CASE WHEN rn = 1 THEN ""Name"" END)            AS keep_name,
                MAX(CASE WHEN rn = 1 THEN ""CurrentStage"" END)    AS keep_stage,
                bool_or(rn = 1 AND ""HasPayment"")                  AS keep_haspay,
                bool_or(rn = 1 AND ""HasAppointment"")              AS keep_hasappt,
                MAX(CASE WHEN rn = 1 THEN ""Price"" END)           AS keep_price,
                MAX(CASE WHEN rn = 1 THEN ""CreatedAt"" END)       AS keep_created,
                COALESCE(ARRAY_AGG(""Id""  ORDER BY rn) FILTER (WHERE rn > 1), ARRAY[]::int[])  AS delete_ids,
                COALESCE(ARRAY_AGG(""Name"" ORDER BY rn) FILTER (WHERE rn > 1), ARRAY[]::text[]) AS delete_names
            FROM ranked
            WHERE grp_count > 1
            GROUP BY phone, grp_count
            ORDER BY grp_count DESC, phone
            LIMIT 5000;";

        var conn = _db.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open) await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        if (useParam) AddTenantParam(cmd, tenantId!.Value);

        var result = new List<LeadDuplicateGroupDto>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var rawCount = reader.GetValue(2);
            var count = rawCount is long l ? (int)l : Convert.ToInt32(rawCount);

            result.Add(new LeadDuplicateGroupDto
            {
                TenantId = reader.IsDBNull(0) ? 0 : reader.GetInt32(0),
                PhoneNormalized = reader.IsDBNull(1) ? "" : reader.GetString(1),
                Count = count,
                KeepLeadId = reader.IsDBNull(3) ? 0 : reader.GetInt32(3),
                KeepName = reader.IsDBNull(4) ? "" : reader.GetString(4),
                KeepStage = reader.IsDBNull(5) ? null : reader.GetString(5),
                KeepHasPayment = !reader.IsDBNull(6) && reader.GetBoolean(6),
                KeepHasAppointment = !reader.IsDBNull(7) && reader.GetBoolean(7),
                KeepPrice = reader.IsDBNull(8) ? null : reader.GetDecimal(8),
                KeepCreatedAt = reader.IsDBNull(9) ? default : reader.GetDateTime(9),
                DeleteLeadIds = ((int[])reader.GetValue(10)).ToList(),
                DeleteNames = ((string[])reader.GetValue(11)).Where(s => s != null).ToList(),
            });
        }
        return result;
    }

    // ─── Helpers de SQL ──────────────────────────────────────────────────────

    private static (string Partition, string Filter, bool UseParam, string KeyExpr) BuildPartitionFilter(
        int? tenantId, bool ignoreTenant, string mode)
    {
        // Chave de agrupamento + filtro de validade por modo.
        var (keyExpr, validity) = mode == ModeName
            ? (NameExpr, $"WHERE length({NameExpr}) >= 2")
            : (PhoneExpr, $"WHERE length({PhoneExpr}) >= {MinPhoneDigits}");

        if (ignoreTenant)
            return (keyExpr, validity, false, keyExpr);

        if (tenantId.HasValue)
            return ($"\"TenantId\", {keyExpr}", $"{validity} AND \"TenantId\" = @p0", true, keyExpr);

        return ($"\"TenantId\", {keyExpr}", validity, false, keyExpr);
    }

    private static void AddTenantParam(System.Data.Common.DbCommand cmd, int tenantId)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = "@p0";
        p.Value = tenantId;
        cmd.Parameters.Add(p);
    }
}
