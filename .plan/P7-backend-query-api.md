# P7 — Backend: Query Endpoints + Diff + Inventory

**Phase:** 2 | **Phụ thuộc:** P6 | **Output:** REST API đầy đủ cho dashboard và tooling

## Mục tiêu

Implement tất cả GET endpoints: hosts list, snapshot latest, diff, violations, app inventory. Bao gồm pagination, filtering, và repositories tương ứng.

## Files cần tạo

```
src/PolicyCollector.Backend/
  Api/
    Endpoints/
      HostsEndpoint.cs
      SnapshotEndpoint.cs
      DiffEndpoint.cs
      ViolationsEndpoint.cs
      InventoryEndpoint.cs
    Models/
      HostSummary.cs
      DiffResponse.cs
      ViolationsResponse.cs
      InventoryResponse.cs
      PaginatedResponse.cs
  Data/
    Repositories/
      SnapshotRepository.cs    (bổ sung GET methods)
      ViolationRepository.cs   (bổ sung GET methods)
      HostRepository.cs        (bổ sung GET methods)
      AppInventoryRepository.cs

tests/PolicyCollector.Backend.Tests/
  Api/
    HostsEndpointTests.cs
    DiffEndpointTests.cs
    ViolationsEndpointTests.cs
    InventoryEndpointTests.cs
```

---

## Chi tiết từng file

### [FILE] `Api/Models/PaginatedResponse.cs`

```csharp
namespace PolicyCollector.Backend.Api.Models;

public sealed record PaginatedResponse<T>(
    int Total,
    int Page,
    int Size,
    IReadOnlyList<T> Items);
```

---

### [FILE] `Api/Models/HostSummary.cs`

```csharp
namespace PolicyCollector.Backend.Api.Models;

public sealed record HostSummary(
    [property: JsonPropertyName("hostname")]       string Hostname,
    [property: JsonPropertyName("domain")]         string? Domain,
    [property: JsonPropertyName("os_version")]     string? OsVersion,
    [property: JsonPropertyName("agent_version")]  string? AgentVersion,
    [property: JsonPropertyName("last_seen")]      DateTimeOffset? LastSeen,
    [property: JsonPropertyName("status")]         string Status,         // online | offline | unknown
    [property: JsonPropertyName("violation_count")] int ViolationCount
);
```

---

### [FILE] `Api/Endpoints/HostsEndpoint.cs`

```csharp
namespace PolicyCollector.Backend.Api.Endpoints;

public static class HostsEndpoints
{
    public static void MapHostsEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1").RequireAuthorization();

        // GET /api/v1/hosts
        group.MapGet("/hosts", GetHosts)
             .Produces<PaginatedResponse<HostSummary>>(200);

        // GET /api/v1/hosts/{hostname}/latest
        group.MapGet("/hosts/{hostname}/latest", GetLatest)
             .Produces<CollectionPayload>(200)
             .Produces(404);
    }

    private static async Task<IResult> GetHosts(
        [FromQuery] string? domain,
        [FromQuery] string? status,
        [FromQuery] int page = 1,
        [FromQuery] int size = 50,
        [FromQuery] string sort = "last_seen",
        [FromQuery] string order = "desc",
        HostRepository hosts = null!,
        ViolationRepository violations = null!,
        CancellationToken ct = default)
    {
        if (size > 200) size = 200;
        if (page < 1) page = 1;

        var (total, items) = await hosts.GetPagedAsync(
            domain, status, sort, order, page, size, ct);

        // Tính online/offline status (last_seen < 2h = online, > 24h = offline)
        var now = DateTimeOffset.UtcNow;
        var summaries = new List<HostSummary>();

        foreach (var h in items)
        {
            var hostStatus = h.LastSeen switch
            {
                null => "unknown",
                var ls when ls > now.AddHours(-2) => "online",
                var ls when ls < now.AddHours(-24) => "offline",
                _ => "stale"
            };

            // Đếm open violations
            var violationCount = await violations.CountOpenAsync(h.Hostname, ct);

            summaries.Add(new HostSummary(
                h.Hostname, h.Domain, h.OsVersion, h.AgentVersion,
                h.LastSeen, hostStatus, violationCount));
        }

        return Results.Ok(new PaginatedResponse<HostSummary>(total, page, size, summaries));
    }

    private static async Task<IResult> GetLatest(
        string hostname,
        SnapshotRepository snapshots,
        CancellationToken ct)
    {
        var snapshot = await snapshots.GetLatestAsync(hostname, ct);
        if (snapshot is null) return Results.NotFound();

        // Deserialize payload từ JSONB
        var payload = JsonSerializer.Deserialize(
            snapshot.PayloadJson, JsonContext.Default.CollectionPayload);

        return Results.Ok(payload);
    }
}
```

---

### [FILE] `Api/Endpoints/DiffEndpoint.cs`

```csharp
namespace PolicyCollector.Backend.Api.Endpoints;

public static class DiffEndpoints
{
    public static void MapDiffEndpoints(this WebApplication app)
    {
        app.MapGet("/api/v1/hosts/{hostname}/diff", GetDiff)
           .Produces<DiffResponse>(200)
           .Produces(404);
    }

    private static async Task<IResult> GetDiff(
        string hostname,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        SnapshotRepository snapshots,
        ConfigChangeRepository changes,
        CancellationToken ct)
    {
        // Lấy snapshot "to" (default: latest)
        var toSnapshot = to.HasValue
            ? await snapshots.GetNearestAsync(hostname, to.Value, ct)
            : await snapshots.GetLatestAsync(hostname, ct);

        if (toSnapshot is null) return Results.NotFound();

        // Lấy snapshot "from" (default: snapshot trước "to")
        var fromSnapshot = from.HasValue
            ? await snapshots.GetNearestAsync(hostname, from.Value, ct)
            : await snapshots.GetPreviousAsync(hostname, toSnapshot.Id, ct);

        if (fromSnapshot is null)
            return Results.Ok(new DiffResponse(hostname,
                null, toSnapshot.Id, null, toSnapshot.CollectedAt, []));

        // Lấy config_changes giữa 2 snapshot
        var changeList = await changes.GetBetweenSnapshotsAsync(
            hostname, fromSnapshot.Id, toSnapshot.Id, ct);

        return Results.Ok(new DiffResponse(
            hostname,
            fromSnapshot.Id,
            toSnapshot.Id,
            fromSnapshot.CollectedAt,
            toSnapshot.CollectedAt,
            changeList));
    }
}

public sealed record DiffResponse(
    [property: JsonPropertyName("hostname")]       string Hostname,
    [property: JsonPropertyName("from_snapshot")]  Guid? FromSnapshot,
    [property: JsonPropertyName("to_snapshot")]    Guid ToSnapshot,
    [property: JsonPropertyName("from_time")]      DateTimeOffset? FromTime,
    [property: JsonPropertyName("to_time")]        DateTimeOffset ToTime,
    [property: JsonPropertyName("changes")]        IReadOnlyList<ConfigChangeDto> Changes
);

public sealed record ConfigChangeDto(
    [property: JsonPropertyName("field_path")] string FieldPath,
    [property: JsonPropertyName("old_value")]  string? OldValue,
    [property: JsonPropertyName("new_value")]  string? NewValue,
    [property: JsonPropertyName("changed_at")] DateTimeOffset ChangedAt
);
```

---

### [FILE] `Api/Endpoints/ViolationsEndpoint.cs`

```csharp
namespace PolicyCollector.Backend.Api.Endpoints;

public static class ViolationsEndpoints
{
    public static void MapViolationsEndpoints(this WebApplication app)
    {
        app.MapGet("/api/v1/policy/violations", GetViolations)
           .Produces<PaginatedResponse<ViolationDto>>(200);
    }

    private static async Task<IResult> GetViolations(
        [FromQuery] string? hostname,
        [FromQuery] string? severity,
        [FromQuery] string? ruleId,
        [FromQuery] bool resolved = false,
        [FromQuery] int page = 1,
        [FromQuery] int size = 50,
        ViolationRepository violations = null!,
        CancellationToken ct = default)
    {
        if (size > 200) size = 200;

        var (total, items) = await violations.GetPagedAsync(
            hostname, severity, ruleId, resolved, page, size, ct);

        var dtos = items.Select(v => new ViolationDto(
            v.Id, v.SnapshotId, v.Hostname, v.DetectedAt,
            v.RuleId, v.Severity, v.Message, v.Expected, v.Actual,
            v.Resolved, v.ResolvedAt)).ToList();

        return Results.Ok(new PaginatedResponse<ViolationDto>(total, page, size, dtos));
    }
}
```

---

### [FILE] `Api/Endpoints/InventoryEndpoint.cs`

```csharp
namespace PolicyCollector.Backend.Api.Endpoints;

public static class InventoryEndpoints
{
    public static void MapInventoryEndpoints(this WebApplication app)
    {
        app.MapGet("/api/v1/apps/inventory", GetInventory)
           .Produces<PaginatedResponse<AppInventoryDto>>(200);
    }

    private static async Task<IResult> GetInventory(
        [FromQuery] string? name,
        [FromQuery] string? publisher,
        [FromQuery] string? hostname,
        [FromQuery] int page = 1,
        [FromQuery] int size = 100,
        AppInventoryRepository inventory = null!,
        CancellationToken ct = default)
    {
        if (size > 500) size = 500;

        var (total, items) = await inventory.GetPagedAsync(
            name, publisher, hostname, page, size, ct);

        return Results.Ok(new PaginatedResponse<AppInventoryDto>(total, page, size, items));
    }
}

public sealed record AppInventoryDto(
    [property: JsonPropertyName("display_name")]    string DisplayName,
    [property: JsonPropertyName("version")]         string? Version,
    [property: JsonPropertyName("publisher")]       string? Publisher,
    [property: JsonPropertyName("machine_count")]   int MachineCount,
    [property: JsonPropertyName("last_seen")]       DateTimeOffset? LastSeen
);
```

---

### [FILE] `Data/Repositories/SnapshotRepository.cs`

```csharp
namespace PolicyCollector.Backend.Data.Repositories;

public sealed class SnapshotRepository
{
    private readonly IDbConnectionFactory _db;

    // Insert snapshot, ON CONFLICT collection_id DO NOTHING (idempotent)
    public async Task<Guid> UpsertAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        CollectionPayload payload,
        CancellationToken ct)
    {
        var id = Guid.NewGuid();
        var json = JsonSerializer.Serialize(payload, JsonContext.Default.CollectionPayload);

        await conn.ExecuteAsync("""
            INSERT INTO collection_snapshots
                (id, collection_id, hostname, domain, os_version, agent_version,
                 schema_version, collected_at, payload)
            VALUES
                (@id, @collectionId, @hostname, @domain, @osVersion, @agentVersion,
                 @schemaVersion, @collectedAt, @payload::jsonb)
            ON CONFLICT (collection_id) DO NOTHING
            """,
            new {
                id, collectionId = Guid.Parse(payload.CollectionId),
                hostname = payload.Host?.Hostname,
                domain = payload.Host?.Domain,
                osVersion = payload.Host?.OsVersion,
                agentVersion = payload.AgentVersion,
                schemaVersion = payload.SchemaVersion,
                collectedAt = payload.CollectedAt,
                payload = json
            }, transaction: tx);

        return id;
    }

    // Lấy snapshot mới nhất của hostname
    public async Task<SnapshotRow?> GetLatestAsync(string hostname, CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);
        return await conn.QueryFirstOrDefaultAsync<SnapshotRow>("""
            SELECT id, collection_id, hostname, collected_at, payload
            FROM collection_snapshots
            WHERE hostname = @hostname
            ORDER BY collected_at DESC
            LIMIT 1
            """, new { hostname });
    }

    // Lấy snapshot trước snapshot có id cho trước
    public async Task<SnapshotRow?> GetPreviousAsync(
        NpgsqlConnection conn, string hostname, Guid currentId, CancellationToken ct)
    {
        return await conn.QueryFirstOrDefaultAsync<SnapshotRow>("""
            SELECT id, hostname, collected_at, payload
            FROM collection_snapshots
            WHERE hostname = @hostname AND id != @currentId
            ORDER BY collected_at DESC
            LIMIT 1
            """, new { hostname, currentId });
    }

    // Lấy snapshot gần nhất với thời điểm t
    public async Task<SnapshotRow?> GetNearestAsync(
        string hostname, DateTimeOffset t, CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);
        return await conn.QueryFirstOrDefaultAsync<SnapshotRow>("""
            SELECT id, hostname, collected_at, payload
            FROM collection_snapshots
            WHERE hostname = @hostname AND collected_at <= @t
            ORDER BY collected_at DESC
            LIMIT 1
            """, new { hostname, t });
    }
}
```

---

### [FILE] `Data/Repositories/AppInventoryRepository.cs`

```csharp
namespace PolicyCollector.Backend.Data.Repositories;

public sealed class AppInventoryRepository
{
    private readonly IDbConnectionFactory _db;

    // Query app inventory từ latest snapshots — JSONB query
    public async Task<(int Total, IReadOnlyList<AppInventoryDto> Items)> GetPagedAsync(
        string? name, string? publisher, string? hostname,
        int page, int size, CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);

        // PostgreSQL query: unnest applications array từ latest snapshot của mỗi máy
        // Dùng JSONB function: jsonb_array_elements
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

        var offset = (page - 1) * size;
        var items = await conn.QueryAsync<AppInventoryDto>(sql,
            new { name, publisher, hostname, size, offset });

        // Count query
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

        var total = await conn.ExecuteScalarAsync<int>(countSql, new { name, publisher, hostname });

        return (total, items.ToList());
    }
}
```

---

## Unit Tests

### [TEST] `Api/HostsEndpointTests.cs`

```csharp
// WebApplicationFactory
// Test cases:
//   - GET /hosts → 200 + paginated list
//   - GET /hosts?domain=corp.local → filter theo domain
//   - GET /hosts?status=online → filter theo status
//   - GET /hosts?page=2&size=10 → pagination correct
//   - GET /hosts?size=300 → capped at 200
//   - GET /hosts/{hostname}/latest → 200 + full payload
//   - GET /hosts/{notexist}/latest → 404
```

### [TEST] `Api/DiffEndpointTests.cs`

```csharp
// Test cases:
//   - GET /diff với 2 snapshots → changes list
//   - GET /diff khi chỉ có 1 snapshot → empty changes
//   - GET /diff với ?from=... ?to=... params
//   - GET /diff hostname không tồn tại → 404
```

---

## Acceptance Criteria

- [ ] `GET /api/v1/hosts` trả về paginated list, default page=1 size=50
- [ ] Status `online` khi `last_seen < 2h`, `offline` khi `last_seen > 24h`
- [ ] `GET /api/v1/hosts/{hostname}/latest` trả về full CollectionPayload JSON
- [ ] `GET /api/v1/hosts/{hostname}/diff` phát hiện thay đổi password policy
- [ ] `GET /api/v1/policy/violations?resolved=false` chỉ trả unresolved violations
- [ ] `GET /api/v1/apps/inventory?name=chrome` tìm kiếm ILIKE (case insensitive)
- [ ] App inventory aggregate: 1 app name → nhiều máy → machine_count đúng
- [ ] Tất cả endpoints trả 401 khi thiếu X-Api-Key header
- [ ] Pagination không vượt max (hosts: 200, inventory: 500)
