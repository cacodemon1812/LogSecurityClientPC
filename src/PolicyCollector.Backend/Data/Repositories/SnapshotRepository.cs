using Dapper;
using Npgsql;
using PolicyCollector.Backend.Data.Models;

namespace PolicyCollector.Backend.Data.Repositories;

public sealed class SnapshotRepository
{
    private readonly IDbConnectionFactory _db;
    private readonly ILogger<SnapshotRepository> _logger;

    public SnapshotRepository(IDbConnectionFactory db, ILogger<SnapshotRepository> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<NpgsqlConnection> OpenConnectionAsync(CancellationToken ct)
        => await _db.OpenAsync(ct);

    public async Task<Guid> UpsertAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        CollectionPayload payload,
        CancellationToken ct)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(payload);

        // TimescaleDB hypertable: unique index is (collection_id, collected_at)
        var id = await conn.QuerySingleOrDefaultAsync<Guid?>("""
            INSERT INTO collection_snapshots
                (collection_id, hostname, domain, os_version, agent_version, schema_version, collected_at, payload)
            VALUES (@CollectionId, @Hostname, @Domain, @OsVersion, @AgentVersion, @SchemaVersion, @CollectedAt, @Payload::jsonb)
            ON CONFLICT (collection_id, collected_at) DO NOTHING
            RETURNING id
            """, new
        {
            CollectionId = Guid.Parse(payload.CollectionId),
            Hostname = payload.Host?.Hostname,
            Domain = payload.Host?.Domain,
            OsVersion = payload.Host?.OsVersion,
            AgentVersion = payload.AgentVersion,
            SchemaVersion = payload.SchemaVersion,
            CollectedAt = payload.CollectedAt,
            Payload = json
        }, transaction: tx);

        if (id is null)
        {
            // Duplicate (collection_id, collected_at) — return the existing snapshot id
            id = await conn.QuerySingleAsync<Guid>(
                "SELECT id FROM collection_snapshots WHERE collection_id = @CollectionId ORDER BY received_at DESC LIMIT 1",
                new { CollectionId = Guid.Parse(payload.CollectionId) }, transaction: tx);
            _logger.LogDebug("Duplicate collection_id {CollectionId}, reusing snapshot {Id}", payload.CollectionId, id);
        }
        else
        {
            _logger.LogDebug("Snapshot inserted: {Id}", id);
        }

        return id.Value;
    }

    public async Task<CollectionPayload?> GetPreviousAsync(
        NpgsqlConnection conn,
        string hostname,
        Guid currentSnapshotId,
        CancellationToken ct)
    {
        var json = await conn.QuerySingleOrDefaultAsync<string>("""
            SELECT payload FROM collection_snapshots
            WHERE hostname = @Hostname AND id != @CurrentId
            ORDER BY collected_at DESC
            LIMIT 1
            """, new { Hostname = hostname, CurrentId = currentSnapshotId });

        if (string.IsNullOrEmpty(json))
            return null;

        return System.Text.Json.JsonSerializer.Deserialize<CollectionPayload>(json);
    }

    public async Task<SnapshotRow?> GetLatestAsync(string hostname, CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);
        return await conn.QueryFirstOrDefaultAsync<SnapshotRow>("""
            SELECT id, collection_id, hostname, collected_at, payload as PayloadJson
            FROM collection_snapshots
            WHERE hostname = @Hostname
            ORDER BY collected_at DESC
            LIMIT 1
            """, new { Hostname = hostname });
    }

    public async Task<SnapshotRow?> GetLatestByHostIdAsync(Guid hostId, CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);
        return await conn.QueryFirstOrDefaultAsync<SnapshotRow>("""
            SELECT cs.id, cs.collection_id, cs.hostname, cs.collected_at, cs.payload AS PayloadJson
            FROM collection_snapshots cs
            INNER JOIN host_latest hl ON hl.hostname = cs.hostname
            WHERE hl.host_id = @HostId
            ORDER BY cs.collected_at DESC
            LIMIT 1
            """, new { HostId = hostId });
    }

    public async Task<SnapshotRow?> GetNearestAsync(
        string hostname, DateTimeOffset t, CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);
        return await conn.QueryFirstOrDefaultAsync<SnapshotRow>("""
            SELECT id, collection_id, hostname, collected_at, payload as PayloadJson
            FROM collection_snapshots
            WHERE hostname = @Hostname AND collected_at <= @Time
            ORDER BY collected_at DESC
            LIMIT 1
            """, new { Hostname = hostname, Time = t });
    }

    public async Task<SnapshotRow?> GetNearestByHostIdAsync(
        Guid hostId, DateTimeOffset t, CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);
        return await conn.QueryFirstOrDefaultAsync<SnapshotRow>("""
            SELECT cs.id, cs.collection_id, cs.hostname, cs.collected_at, cs.payload AS PayloadJson
            FROM collection_snapshots cs
            INNER JOIN host_latest hl ON hl.hostname = cs.hostname
            WHERE hl.host_id = @HostId AND cs.collected_at <= @Time
            ORDER BY cs.collected_at DESC
            LIMIT 1
            """, new { HostId = hostId, Time = t });
    }

    public async Task<SnapshotRow?> GetPreviousAsync(
        string hostname, Guid currentSnapshotId, CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);
        return await conn.QueryFirstOrDefaultAsync<SnapshotRow>("""
            SELECT id, collection_id, hostname, collected_at, payload as PayloadJson
            FROM collection_snapshots
            WHERE hostname = @Hostname AND id != @CurrentId
            ORDER BY collected_at DESC
            LIMIT 1
            """, new { Hostname = hostname, CurrentId = currentSnapshotId });
    }

    public async Task<SnapshotRow?> GetPreviousByHostIdAsync(
        Guid hostId, Guid currentSnapshotId, CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);
        return await conn.QueryFirstOrDefaultAsync<SnapshotRow>("""
            SELECT cs.id, cs.collection_id, cs.hostname, cs.collected_at, cs.payload AS PayloadJson
            FROM collection_snapshots cs
            INNER JOIN host_latest hl ON hl.hostname = cs.hostname
            WHERE hl.host_id = @HostId AND cs.id != @CurrentId
            ORDER BY cs.collected_at DESC
            LIMIT 1
            """, new { HostId = hostId, CurrentId = currentSnapshotId });
    }
}
