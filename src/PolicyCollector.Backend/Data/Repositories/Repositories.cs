using Dapper;
using Npgsql;
using PolicyCollector.Backend.Data.Models;
using PolicyCollector.Backend.Api.Models;

namespace PolicyCollector.Backend.Data.Repositories;

public sealed class ConfigChangeRepository
{
    private readonly IDbConnectionFactory _db;
    private readonly ILogger<ConfigChangeRepository> _logger;

    public ConfigChangeRepository(IDbConnectionFactory db, ILogger<ConfigChangeRepository> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task InsertBatchAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        List<ConfigChange> changes,
        Guid snapshotAfterId,
        CancellationToken ct)
    {
        if (changes.Count == 0)
            return;

        var sql = """
            INSERT INTO config_changes (hostname, changed_at, field_path, old_value, new_value, snapshot_after)
            VALUES (@Hostname, @ChangedAt, @FieldPath, @OldValue, @NewValue, @SnapshotAfterId)
            """;

        foreach (var change in changes)
        {
            await conn.ExecuteAsync(sql, new
            {
                change.Hostname,
                change.ChangedAt,
                change.FieldPath,
                change.OldValue,
                change.NewValue,
                SnapshotAfterId = snapshotAfterId
            }, transaction: tx);
        }

        _logger.LogInformation("Inserted {Count} config changes", changes.Count);
    }

    public async Task<IReadOnlyList<ConfigChangeDto>> GetBetweenSnapshotsAsync(
        string hostname,
        Guid fromSnapshotId,
        Guid toSnapshotId,
        CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);

        var changes = await conn.QueryAsync<ConfigChangeDto>("""
            SELECT field_path, old_value, new_value, changed_at
            FROM config_changes
            WHERE hostname = @Hostname
              AND changed_at >= (
                SELECT collected_at FROM collection_snapshots WHERE id = @FromId
              )
              AND changed_at <= (
                SELECT collected_at FROM collection_snapshots WHERE id = @ToId
              )
            ORDER BY changed_at ASC
            """, new { Hostname = hostname, FromId = fromSnapshotId, ToId = toSnapshotId });

        return changes.ToList();
    }
}

public sealed class ViolationRepository
{
    private readonly IDbConnectionFactory _db;
    private readonly ILogger<ViolationRepository> _logger;

    public ViolationRepository(IDbConnectionFactory db, ILogger<ViolationRepository> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task InsertNewAsync(
        Guid snapshotId,
        List<ViolationEntry> violations,
        CancellationToken ct)
    {
        if (violations.Count == 0)
            return;

        await using var conn = await _db.OpenAsync(ct);

        var sql = """
            INSERT INTO policy_violations (snapshot_id, hostname, rule_id, severity, message, expected, actual)
            VALUES (@SnapshotId, @Hostname, @RuleId, @Severity, @Message, @Expected, @Actual)
            """;

        foreach (var violation in violations)
        {
            await conn.ExecuteAsync(sql, new
            {
                SnapshotId = snapshotId,
                violation.Hostname,
                violation.RuleId,
                violation.Severity,
                violation.Message,
                violation.Expected,
                violation.Actual
            });
        }

        _logger.LogInformation("Inserted {Count} violations for snapshot {SnapshotId}", violations.Count, snapshotId);
    }

    public async Task<int> CountOpenAsync(string hostname, CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);
        return await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM policy_violations WHERE hostname = @Hostname AND resolved = FALSE",
            new { Hostname = hostname });
    }

    public async Task<(int Total, IReadOnlyList<ViolationRow> Items)> GetPagedAsync(
        string? hostname,
        string? severity,
        string? ruleId,
        bool resolved,
        int page,
        int size,
        CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);

        var whereConditions = new List<string> { "resolved = @Resolved" };
        var queryParams = new DynamicParameters();
        queryParams.Add("@Resolved", resolved);

        if (!string.IsNullOrEmpty(hostname))
        {
            whereConditions.Add("hostname = @Hostname");
            queryParams.Add("@Hostname", hostname);
        }

        if (!string.IsNullOrEmpty(severity))
        {
            whereConditions.Add("severity = @Severity");
            queryParams.Add("@Severity", severity);
        }

        if (!string.IsNullOrEmpty(ruleId))
        {
            whereConditions.Add("rule_id = @RuleId");
            queryParams.Add("@RuleId", ruleId);
        }

        var whereClause = string.Join(" AND ", whereConditions);
        var offset = (page - 1) * size;
        queryParams.Add("@Offset", offset);
        queryParams.Add("@Size", size);

        var items = await conn.QueryAsync<ViolationRow>($"""
            SELECT id, snapshot_id, hostname, detected_at, rule_id, severity, message, expected, actual, resolved, resolved_at
            FROM policy_violations
            WHERE {whereClause}
            ORDER BY detected_at DESC
            LIMIT @Size OFFSET @Offset
            """, queryParams);

        var countSql = $"SELECT COUNT(*) FROM policy_violations WHERE {whereClause}";
        var total = await conn.ExecuteScalarAsync<int>(countSql, queryParams);

        return (total, items.ToList());
    }
}

public sealed class PolicyRuleRepository
{
    private readonly IDbConnectionFactory _db;
    private readonly IMemoryCache _cache;
    private const string CacheKey = "policy_rules_enabled";

    public PolicyRuleRepository(IDbConnectionFactory db, IMemoryCache cache)
    {
        _db = db;
        _cache = cache;
    }

    public async Task<IReadOnlyList<PolicyRule>> GetEnabledRulesAsync(CancellationToken ct)
    {
        if (_cache.TryGetValue(CacheKey, out IReadOnlyList<PolicyRule>? cached))
            return cached ?? [];

        await using var conn = await _db.OpenAsync(ct);
        var rules = (await conn.QueryAsync<PolicyRule>(
            "SELECT id, rule_id, severity, description, enabled FROM policy_rules WHERE enabled = TRUE"))
            .ToList();

        _cache.Set(CacheKey, (IReadOnlyList<PolicyRule>)rules, TimeSpan.FromMinutes(5));
        return rules;
    }

    public async Task<IReadOnlyList<PolicyRule>> GetAllRulesAsync(CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);
        var rules = await conn.QueryAsync<PolicyRule>(
            "SELECT id, rule_id, severity, description, enabled FROM policy_rules ORDER BY rule_id");
        return rules.ToList();
    }

    public async Task UpdateAsync(string ruleId, bool enabled, string? severity, CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);
        await conn.ExecuteAsync("""
            UPDATE policy_rules
            SET enabled = @enabled,
                severity = COALESCE(@severity, severity),
                updated_at = NOW()
            WHERE rule_id = @ruleId
            """, new { ruleId, enabled, severity });

        _cache.Remove(CacheKey);
    }
}

public sealed class AppInventoryRepository
{
    private readonly IDbConnectionFactory _db;

    public AppInventoryRepository(IDbConnectionFactory db)
    {
        _db = db;
    }

    public async Task<(int Total, IReadOnlyList<AppInventoryDto> Items)> GetPagedAsync(
        string? name,
        string? publisher,
        string? hostname,
        int page,
        int size,
        CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);

        var offset = (page - 1) * size;

        var sql = """
            WITH latest AS (
                SELECT DISTINCT ON (hostname) hostname, payload
                FROM collection_snapshots
                ORDER BY hostname, collected_at DESC
            ),
            apps AS (
                SELECT
                    hostname,
                    app->>'display_name' AS display_name,
                    app->>'display_version' AS version,
                    app->>'publisher' AS publisher
                FROM latest, jsonb_array_elements(payload->'applications') AS app
                WHERE payload->'applications' IS NOT NULL
            )
            SELECT
                display_name,
                MAX(version) AS version,
                publisher,
                COUNT(DISTINCT hostname) AS machine_count,
                NULL::timestamptz AS last_seen
            FROM apps
            WHERE
                (@name IS NULL OR display_name ILIKE '%' || @name || '%')
                AND (@publisher IS NULL OR publisher ILIKE '%' || @publisher || '%')
                AND (@hostname IS NULL OR hostname = @hostname)
            GROUP BY display_name, publisher
            ORDER BY machine_count DESC, display_name
            LIMIT @size OFFSET @offset
            """;

        var items = await conn.QueryAsync<AppInventoryDto>(sql,
            new { name, publisher, hostname, size, offset });

        var countSql = """
            WITH latest AS (
                SELECT DISTINCT ON (hostname) hostname, payload
                FROM collection_snapshots ORDER BY hostname, collected_at DESC
            ),
            apps AS (
                SELECT app->>'display_name' AS display_name, app->>'publisher' AS publisher, hostname
                FROM latest, jsonb_array_elements(payload->'applications') AS app
                WHERE payload->'applications' IS NOT NULL
            )
            SELECT COUNT(DISTINCT display_name)
            FROM apps
            WHERE (@name IS NULL OR display_name ILIKE '%' || @name || '%')
              AND (@publisher IS NULL OR publisher ILIKE '%' || @publisher || '%')
              AND (@hostname IS NULL OR hostname = @hostname)
            """;

        var total = await conn.ExecuteScalarAsync<int>(countSql,
            new { name, publisher, hostname });

        return (total, items.ToList());
    }
}
