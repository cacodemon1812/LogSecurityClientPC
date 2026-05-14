using Dapper;
using Npgsql;
using PolicyCollector.Backend.Data.Models;

namespace PolicyCollector.Backend.Data.Repositories;

public sealed class HostRepository
{
    private readonly IDbConnectionFactory _db;
    private readonly ILogger<HostRepository> _logger;

    public HostRepository(IDbConnectionFactory db, ILogger<HostRepository> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task UpsertAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        CollectionPayload payload,
        Guid snapshotId,
        CancellationToken ct)
    {
        var hostname = payload.Host?.Hostname ?? "unknown";

        await conn.ExecuteAsync("""
            INSERT INTO host_latest (hostname, domain, last_seen, agent_version, os_version, snapshot_id, status)
            VALUES (@Hostname, @Domain, @LastSeen, @AgentVersion, @OsVersion, @SnapshotId, @Status)
            ON CONFLICT (hostname) DO UPDATE SET
                domain = EXCLUDED.domain,
                last_seen = EXCLUDED.last_seen,
                agent_version = EXCLUDED.agent_version,
                os_version = EXCLUDED.os_version,
                snapshot_id = EXCLUDED.snapshot_id,
                status = EXCLUDED.status
            """, new
        {
            Hostname = hostname,
            Domain = payload.Host?.Domain,
            LastSeen = payload.CollectedAt,
            AgentVersion = payload.AgentVersion,
            OsVersion = payload.Host?.OsVersion,
            SnapshotId = snapshotId,
            Status = "online"
        }, transaction: tx);

        _logger.LogDebug("Host upserted: {Hostname}", hostname);
    }

    public async Task<(int Total, IReadOnlyList<HostRow> Items)> GetPagedAsync(
        string? domain,
        string? status,
        string sort,
        string order,
        int page,
        int size,
        CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);

        var allowedColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "hostname", "domain", "last_seen", "os_version", "agent_version", "status" };
        var safeColumn = allowedColumns.Contains(sort) ? sort : "last_seen";
        var safeOrder  = order.Equals("asc", StringComparison.OrdinalIgnoreCase) ? "ASC" : "DESC";
        var orderClause = $"{safeColumn} {safeOrder}";

        var whereConditions = new List<string>();
        var queryParams = new DynamicParameters();

        if (!string.IsNullOrEmpty(domain))
        {
            whereConditions.Add("domain = @Domain");
            queryParams.Add("@Domain", domain);
        }

        if (!string.IsNullOrEmpty(status))
        {
            whereConditions.Add("status = @Status");
            queryParams.Add("@Status", status);
        }

        var whereClause = whereConditions.Count > 0 ? "WHERE " + string.Join(" AND ", whereConditions) : "";

        var offset = (page - 1) * size;
        queryParams.Add("@Offset", offset);
        queryParams.Add("@Size", size);

        var items = await conn.QueryAsync<HostRow>($"""
            SELECT host_id, hostname, domain, os_version, agent_version, last_seen, status
            FROM host_latest
            {whereClause}
            ORDER BY {orderClause}
            LIMIT @Size OFFSET @Offset
            """, queryParams);

        var countSql = $"SELECT COUNT(*) FROM host_latest {whereClause}";
        var total = await conn.ExecuteScalarAsync<int>(countSql, queryParams);

        return (total, items.ToList());
    }
}
