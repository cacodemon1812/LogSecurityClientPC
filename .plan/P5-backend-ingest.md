# P5 — Backend: Project + Ingest API + DB Migrations

**Phase:** 2 | **Phụ thuộc:** P1 (dùng lại models) | **Output:** Backend nhận payload, lưu DB, trả 202

## Mục tiêu

Tạo ASP.NET Core 8 Minimal API project, thiết lập PostgreSQL + TimescaleDB schema, implement Ingest endpoint với auth + validation + HMAC, enqueue vào Redis Streams.

## Files cần tạo

```
src/PolicyCollector.Backend/
  PolicyCollector.Backend.csproj
  Program.cs
  appsettings.json
  appsettings.Development.json
  Config/
    BackendOptions.cs
  Api/
    Endpoints/
      IngestEndpoint.cs
    Middleware/
      ApiKeyMiddleware.cs
      HmacValidationMiddleware.cs
      RequestLoggingMiddleware.cs
    Models/
      IngestResponse.cs
      ErrorResponse.cs
  Data/
    Migrations/
      001_initial_schema.sql
      002_add_config_changes.sql
      003_add_policy_rules.sql
    MigrationRunner.cs
  Infrastructure/
    RedisQueue.cs
    DatabaseHealthCheck.cs
docker/
  compose.dev.yml
  Dockerfile

tests/PolicyCollector.Backend.Tests/
  PolicyCollector.Backend.Tests.csproj
  Api/
    IngestEndpointTests.cs
  Middleware/
    ApiKeyMiddlewareTests.cs
    HmacValidationMiddlewareTests.cs
```

---

## Chi tiết từng file

### [FILE] `src/PolicyCollector.Backend/PolicyCollector.Backend.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RootNamespace>PolicyCollector.Backend</RootNamespace>
    <Version>0.1.0</Version>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Dapper" Version="2.*" />
    <PackageReference Include="Npgsql" Version="8.*" />
    <PackageReference Include="StackExchange.Redis" Version="2.*" />
    <PackageReference Include="Serilog.AspNetCore" Version="8.*" />
    <PackageReference Include="DbUp-PostgreSQL" Version="5.*" />
    <PackageReference Include="Microsoft.AspNetCore.RateLimiting" Version="8.*" />
  </ItemGroup>
</Project>
```

---

### [FILE] `src/PolicyCollector.Backend/Program.cs`

```csharp
using PolicyCollector.Backend.Api.Endpoints;
using PolicyCollector.Backend.Api.Middleware;
using PolicyCollector.Backend.Config;
using PolicyCollector.Backend.Data;
using PolicyCollector.Backend.Infrastructure;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Serilog
builder.Host.UseSerilog((ctx, cfg) => cfg.ReadFrom.Configuration(ctx.Configuration));

// Config
builder.Services.Configure<BackendOptions>(builder.Configuration.GetSection("Backend"));

// Database
builder.Services.AddSingleton<IDbConnectionFactory, NpgsqlConnectionFactory>();

// Redis
builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
    ConnectionMultiplexer.Connect(builder.Configuration.GetConnectionString("Redis")!));
builder.Services.AddSingleton<RedisQueue>();

// Rate limiting
builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("ingest", cfg =>
    {
        cfg.PermitLimit = 60;
        cfg.Window = TimeSpan.FromMinutes(1);
        cfg.QueueLimit = 0;
    });
});

// Health checks
builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("database")
    .AddCheck<RedisHealthCheck>("redis");

var app = builder.Build();

// Run DB migrations
await MigrationRunner.RunAsync(app.Configuration.GetConnectionString("Postgres")!);

// Middleware pipeline (order matters)
app.UseMiddleware<RequestLoggingMiddleware>();
app.UseMiddleware<ApiKeyMiddleware>();
app.UseMiddleware<HmacValidationMiddleware>();
app.UseRateLimiter();

// Endpoints
app.MapIngestEndpoint();
app.MapHostsEndpoints();   // P7
app.MapHealthChecks("/health");
app.MapHealthChecks("/health/ready", new() { Predicate = _ => true });

app.Run();
```

---

### [FILE] `Api/Middleware/ApiKeyMiddleware.cs`

```csharp
namespace PolicyCollector.Backend.Api.Middleware;

// Validate X-Api-Key header trước khi request đến endpoint
// Bỏ qua /health endpoints
public sealed class ApiKeyMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IDbConnectionFactory _db;
    private readonly ILogger<ApiKeyMiddleware> _logger;

    public async Task InvokeAsync(HttpContext ctx)
    {
        // Bỏ qua health check endpoints
        if (ctx.Request.Path.StartsWithSegments("/health"))
        {
            await _next(ctx);
            return;
        }

        if (!ctx.Request.Headers.TryGetValue("X-Api-Key", out var keyValues))
        {
            ctx.Response.StatusCode = 401;
            await ctx.Response.WriteAsJsonAsync(new ErrorResponse("Missing X-Api-Key header"));
            return;
        }

        var key = keyValues.ToString().Trim();
        if (key.Length < 32)
        {
            ctx.Response.StatusCode = 401;
            await ctx.Response.WriteAsJsonAsync(new ErrorResponse("Invalid API key format"));
            return;
        }

        // Lookup bằng prefix (pc_ + first 8 chars) → hash compare
        var (isValid, keyId) = await ValidateKeyAsync(key);
        if (!isValid)
        {
            _logger.LogWarning("Invalid API key attempt from {IP}, prefix={Prefix}",
                ctx.Connection.RemoteIpAddress, key[..Math.Min(8, key.Length)]);
            ctx.Response.StatusCode = 401;
            await ctx.Response.WriteAsJsonAsync(new ErrorResponse("Invalid API key"));
            return;
        }

        // Gắn key ID vào context để downstream dùng (logging)
        ctx.Items["ApiKeyId"] = keyId;
        await _next(ctx);
    }

    // Validate: hash key, compare với bcrypt hash trong DB
    // Table: api_keys(id, key_hash, prefix, active, created_at, expires_at)
    private async Task<(bool IsValid, int? KeyId)> ValidateKeyAsync(string key) { ··· }
}
```

---

### [FILE] `Api/Middleware/HmacValidationMiddleware.cs`

```csharp
namespace PolicyCollector.Backend.Api.Middleware;

// Validate X-Hmac-SHA256 header (optional — enforce khi HMAC_REQUIRED=true)
public sealed class HmacValidationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly BackendOptions _options;

    public async Task InvokeAsync(HttpContext ctx)
    {
        // Chỉ validate ingest endpoint
        if (!ctx.Request.Path.StartsWithSegments("/api/v1/ingest"))
        {
            await _next(ctx);
            return;
        }

        // Buffer request body để đọc nhiều lần
        ctx.Request.EnableBuffering();

        if (!ctx.Request.Headers.TryGetValue("X-Hmac-SHA256", out var hmacHeader))
        {
            if (_options.HmacRequired)
            {
                ctx.Response.StatusCode = 401;
                await ctx.Response.WriteAsJsonAsync(new ErrorResponse("Missing X-Hmac-SHA256 header"));
                return;
            }
            await _next(ctx);
            return;
        }

        var bodyBytes = await ReadBodyAsync(ctx.Request);
        ctx.Request.Body.Position = 0;  // Reset stream cho controller

        var expectedHmac = ComputeHmac(bodyBytes, _options.HmacSecret);
        var receivedHmac = hmacHeader.ToString();

        // Timing-safe compare
        if (!CryptographicOperations.FixedTimeEquals(
                Convert.FromBase64String(receivedHmac),
                Convert.FromBase64String(expectedHmac)))
        {
            ctx.Response.StatusCode = 401;
            await ctx.Response.WriteAsJsonAsync(new ErrorResponse("HMAC validation failed"));
            return;
        }

        await _next(ctx);
    }

    private static byte[] ComputeHmac(byte[] body, string secret) { ··· }
    private static async Task<byte[]> ReadBodyAsync(HttpRequest request) { ··· }
}
```

---

### [FILE] `Api/Endpoints/IngestEndpoint.cs`

```csharp
namespace PolicyCollector.Backend.Api.Endpoints;

public static class IngestEndpoint
{
    public static void MapIngestEndpoint(this WebApplication app)
    {
        app.MapPost("/api/v1/ingest", HandleIngest)
           .RequireRateLimiting("ingest")
           .Produces<IngestResponse>(202)
           .Produces<ErrorResponse>(400)
           .Produces<ErrorResponse>(401)
           .Produces<ErrorResponse>(422)
           .Produces<ErrorResponse>(503);
    }

    private static async Task<IResult> HandleIngest(
        HttpRequest request,
        RedisQueue queue,
        IOptions<BackendOptions> options,
        ILogger<IngestEndpoint> logger,
        CancellationToken ct)
    {
        // 1. Kiểm tra Content-Type
        if (!request.HasJsonContentType())
            return Results.BadRequest(new ErrorResponse("Content-Type must be application/json"));

        // 2. Kiểm tra kích thước payload (10 MB)
        if (request.ContentLength > 10 * 1024 * 1024)
            return Results.BadRequest(new ErrorResponse("Payload exceeds 10MB limit"));

        // 3. Deserialize
        CollectionPayload? payload;
        try
        {
            payload = await request.ReadFromJsonAsync<CollectionPayload>(
                JsonContext.Default.CollectionPayload, ct);
        }
        catch (JsonException ex)
        {
            return Results.BadRequest(new ErrorResponse($"Invalid JSON: {ex.Message}"));
        }

        if (payload is null)
            return Results.BadRequest(new ErrorResponse("Empty payload"));

        // 4. Validate schema version
        if (!IsSchemaVersionSupported(payload.SchemaVersion))
            return Results.UnprocessableEntity(new ErrorResponse(
                "Schema version not supported",
                $"Supported: {options.Value.SupportedSchemaVersion}"));

        // 5. Validate required fields
        var validationError = ValidatePayload(payload);
        if (validationError is not null)
            return Results.BadRequest(new ErrorResponse(validationError));

        // 6. Enqueue vào Redis Stream
        var ingestionId = Guid.NewGuid().ToString();
        try
        {
            await queue.EnqueueAsync("ingest:queue", payload, ingestionId, ct);
        }
        catch (RedisException ex)
        {
            logger.LogError(ex, "Failed to enqueue payload");
            return Results.StatusCode(503);
        }

        logger.LogInformation("Ingested payload from {Hostname}, collection_id={Id}",
            payload.Host?.Hostname, payload.CollectionId);

        return Results.Accepted(value: new IngestResponse(ingestionId));
    }

    private static bool IsSchemaVersionSupported(string version)
    {
        // Chỉ check major version: "1.0" và "1.1" đều OK nếu supported = "1"
        if (!Version.TryParse(version, out var v)) return false;
        return v.Major == 1;
    }

    private static string? ValidatePayload(CollectionPayload payload)
    {
        if (string.IsNullOrEmpty(payload.CollectionId))
            return "collection_id is required";

        if (!Guid.TryParse(payload.CollectionId, out _))
            return "collection_id must be a valid UUID";

        if (payload.CollectedAt == default)
            return "collected_at is required";

        // Không quá 24h tương lai
        if (payload.CollectedAt > DateTimeOffset.UtcNow.AddHours(24))
            return "collected_at cannot be more than 24h in the future";

        // Không quá 7 ngày quá khứ
        if (payload.CollectedAt < DateTimeOffset.UtcNow.AddDays(-7))
            return "collected_at is too old (> 7 days)";

        if (payload.Host is null)
            return "host is required";

        if (string.IsNullOrWhiteSpace(payload.Host.Hostname))
            return "host.hostname is required";

        if (!HostnameRegex().IsMatch(payload.Host.Hostname))
            return "host.hostname contains invalid characters";

        return null;
    }

    [GeneratedRegex(@"^[a-zA-Z0-9\-\.]{1,255}$")]
    private static partial Regex HostnameRegex();
}
```

---

### [SQL] `Data/Migrations/001_initial_schema.sql`

```sql
-- Extension
CREATE EXTENSION IF NOT EXISTS "uuid-ossp";
CREATE EXTENSION IF NOT EXISTS timescaledb CASCADE;

-- API keys
CREATE TABLE api_keys (
    id          SERIAL PRIMARY KEY,
    key_hash    TEXT NOT NULL,
    prefix      TEXT NOT NULL,   -- First 8 chars plain, for fast lookup
    active      BOOLEAN NOT NULL DEFAULT TRUE,
    created_at  TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    expires_at  TIMESTAMPTZ,
    description TEXT
);
CREATE INDEX idx_api_keys_prefix ON api_keys(prefix) WHERE active = TRUE;

-- Main snapshots table
CREATE TABLE collection_snapshots (
    id              UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    collection_id   UUID NOT NULL UNIQUE,
    hostname        TEXT NOT NULL,
    domain          TEXT,
    os_version      TEXT,
    agent_version   TEXT,
    schema_version  TEXT NOT NULL DEFAULT '1.0',
    collected_at    TIMESTAMPTZ NOT NULL,
    received_at     TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    payload         JSONB NOT NULL
);

CREATE INDEX idx_snapshots_hostname ON collection_snapshots(hostname);
CREATE INDEX idx_snapshots_collected_at ON collection_snapshots(collected_at DESC);
CREATE INDEX idx_snapshots_payload ON collection_snapshots USING GIN(payload);

-- TimescaleDB hypertable
SELECT create_hypertable('collection_snapshots', 'collected_at',
    chunk_time_interval => INTERVAL '7 days');

-- Host latest (denormalized for quick lookup)
CREATE TABLE host_latest (
    hostname        TEXT PRIMARY KEY,
    domain          TEXT,
    last_seen       TIMESTAMPTZ NOT NULL,
    agent_version   TEXT,
    os_version      TEXT,
    snapshot_id     UUID REFERENCES collection_snapshots(id),
    status          TEXT NOT NULL DEFAULT 'unknown'
);

-- Policy violations
CREATE TABLE policy_violations (
    id              BIGSERIAL PRIMARY KEY,
    snapshot_id     UUID NOT NULL REFERENCES collection_snapshots(id),
    hostname        TEXT NOT NULL,
    detected_at     TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    rule_id         TEXT NOT NULL,
    severity        TEXT NOT NULL CHECK (severity IN ('critical','high','medium','low')),
    message         TEXT NOT NULL,
    expected        TEXT,
    actual          TEXT,
    resolved        BOOLEAN NOT NULL DEFAULT FALSE,
    resolved_at     TIMESTAMPTZ
);

CREATE INDEX idx_violations_hostname ON policy_violations(hostname);
CREATE INDEX idx_violations_rule_id ON policy_violations(rule_id);
CREATE INDEX idx_violations_unresolved ON policy_violations(hostname, detected_at)
    WHERE resolved = FALSE;
```

---

### [SQL] `Data/Migrations/002_add_config_changes.sql`

```sql
-- Config change log (diff between snapshots)
CREATE TABLE config_changes (
    id              BIGSERIAL PRIMARY KEY,
    hostname        TEXT NOT NULL,
    changed_at      TIMESTAMPTZ NOT NULL,
    field_path      TEXT NOT NULL,
    old_value       TEXT,
    new_value       TEXT,
    snapshot_before UUID REFERENCES collection_snapshots(id),
    snapshot_after  UUID REFERENCES collection_snapshots(id)
);

CREATE INDEX idx_changes_hostname ON config_changes(hostname, changed_at DESC);
CREATE INDEX idx_changes_field ON config_changes(field_path);
```

---

### [SQL] `Data/Migrations/003_add_policy_rules.sql`

```sql
-- Policy baseline rules (customizable)
CREATE TABLE policy_rules (
    id          SERIAL PRIMARY KEY,
    rule_id     TEXT NOT NULL UNIQUE,
    severity    TEXT NOT NULL CHECK (severity IN ('critical','high','medium','low')),
    description TEXT NOT NULL,
    enabled     BOOLEAN NOT NULL DEFAULT TRUE,
    created_at  TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at  TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- Seed default rules (từ 05-backend-design.md)
INSERT INTO policy_rules (rule_id, severity, description) VALUES
('password.min_length',   'high',     'Password minimum length < 8'),
('password.complexity',   'high',     'Password complexity disabled'),
('password.max_age',      'medium',   'Password max age > 180 days or no expiry'),
('password.lockout',      'high',     'Account lockout threshold = 0 (no lockout)'),
('audit.logon',           'high',     'Logon/Logoff audit not including Failure'),
('firewall.disabled',     'critical', 'Firewall disabled on any profile'),
('defender.realtime',     'critical', 'Windows Defender real-time protection disabled'),
('uac.disabled',          'critical', 'User Account Control (UAC) disabled'),
('bitlocker.os_volume',   'high',     'OS volume (C:) not fully encrypted'),
('tls.weak_protocol',     'high',     'Weak TLS protocol enabled (TLS 1.0 or SSL 3.0)'),
('rdp.nla_disabled',      'high',     'RDP enabled without Network Level Authentication'),
('gpo.not_applied',       'medium',   'Expected GPO not applied'),
('agent.offline',         'medium',   'Agent has not checked in for > 24 hours');
```

---

### [FILE] `Data/MigrationRunner.cs`

```csharp
namespace PolicyCollector.Backend.Data;

public static class MigrationRunner
{
    public static async Task RunAsync(string connectionString)
    {
        // DbUp scans embedded resources matching *.sql in order
        var upgrader = DeployChanges.To
            .PostgresqlDatabase(connectionString)
            .WithScriptsEmbeddedInAssembly(
                Assembly.GetExecutingAssembly(),
                s => s.Contains("Migrations") && s.EndsWith(".sql"))
            .WithTransactionPerScript()
            .LogToConsole()
            .Build();

        var result = upgrader.PerformUpgrade();
        if (!result.Successful)
            throw new InvalidOperationException(
                $"Database migration failed: {result.Error.Message}", result.Error);
    }
}
```

---

### [FILE] `Infrastructure/RedisQueue.cs`

```csharp
namespace PolicyCollector.Backend.Infrastructure;

public sealed class RedisQueue
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisQueue> _logger;

    // Enqueue payload vào Redis Stream
    public async Task EnqueueAsync(
        string streamKey,
        CollectionPayload payload,
        string ingestionId,
        CancellationToken ct)
    {
        var db = _redis.GetDatabase();
        var json = JsonSerializer.Serialize(payload, JsonContext.Default.CollectionPayload);

        // Redis Stream XADD
        await db.StreamAddAsync(streamKey,
        [
            new NameValueEntry("ingestion_id", ingestionId),
            new NameValueEntry("payload", json),
            new NameValueEntry("hostname", payload.Host?.Hostname ?? "unknown"),
            new NameValueEntry("enqueued_at", DateTimeOffset.UtcNow.ToString("O"))
        ]);
    }

    // Consume messages từ stream với consumer group
    public async Task<IReadOnlyList<StreamEntry>> ReadGroupAsync(
        string streamKey,
        string groupName,
        string consumerName,
        int count = 10)
    {
        var db = _redis.GetDatabase();
        // Tạo consumer group nếu chưa có
        try
        {
            await db.StreamCreateConsumerGroupAsync(streamKey, groupName, StreamPosition.NewMessages);
        }
        catch (RedisException) { /* Group đã tồn tại */ }

        var entries = await db.StreamReadGroupAsync(
            streamKey, groupName, consumerName,
            StreamPosition.NewMessages, count);

        return entries;
    }

    // ACK message sau khi xử lý thành công
    public async Task AcknowledgeAsync(string streamKey, string groupName, RedisValue messageId)
    {
        var db = _redis.GetDatabase();
        await db.StreamAcknowledgeAsync(streamKey, groupName, messageId);
    }
}
```

---

### [FILE] `docker/compose.dev.yml`

```yaml
version: "3.9"
services:
  postgres:
    image: timescale/timescaledb:latest-pg16
    restart: unless-stopped
    environment:
      POSTGRES_DB: policycollector
      POSTGRES_USER: pcollector
      POSTGRES_PASSWORD: devpassword123
    ports: ["5432:5432"]
    volumes:
      - pgdata:/var/lib/postgresql/data
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U pcollector"]
      interval: 5s
      timeout: 5s
      retries: 10

  redis:
    image: redis:7-alpine
    restart: unless-stopped
    command: redis-server --requirepass devredispass
    ports: ["6379:6379"]
    healthcheck:
      test: ["CMD", "redis-cli", "-a", "devredispass", "ping"]
      interval: 5s

  backend:
    build:
      context: ../
      dockerfile: docker/Dockerfile
    restart: unless-stopped
    environment:
      ASPNETCORE_ENVIRONMENT: Development
      ConnectionStrings__Postgres: >
        Host=postgres;Port=5432;Database=policycollector;
        Username=pcollector;Password=devpassword123
      ConnectionStrings__Redis: "redis:6379,password=devredispass"
      Backend__ApiKey: "dev-api-key-minimum-32-chars-here"
      Backend__HmacSecret: ""
      Backend__HmacRequired: "false"
    ports: ["8080:8080"]
    depends_on:
      postgres: { condition: service_healthy }
      redis: { condition: service_healthy }

  grafana:
    image: grafana/grafana:latest
    restart: unless-stopped
    ports: ["3000:3000"]
    environment:
      GF_SECURITY_ADMIN_PASSWORD: devgrafana
    volumes:
      - grafana_data:/var/lib/grafana

volumes:
  pgdata:
  grafana_data:
```

---

### [FILE] `docker/Dockerfile`

```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["src/PolicyCollector.Backend/PolicyCollector.Backend.csproj", "src/PolicyCollector.Backend/"]
RUN dotnet restore "src/PolicyCollector.Backend/PolicyCollector.Backend.csproj"
COPY . .
WORKDIR "/src/src/PolicyCollector.Backend"
RUN dotnet publish -c Release -o /app/publish --no-restore

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "PolicyCollector.Backend.dll"]
```

---

## Unit Tests

### [TEST] `Api/IngestEndpointTests.cs`

```csharp
// Dùng WebApplicationFactory<Program>
// Test cases:
//   - POST valid payload + valid API key → 202 + ingestion_id
//   - POST valid payload + missing API key → 401
//   - POST valid payload + invalid API key → 401
//   - POST invalid JSON → 400
//   - POST payload schema_version="2.0" → 422
//   - POST payload size > 10MB → 400
//   - POST payload collected_at > 24h future → 400
//   - POST payload collected_at > 7 days old → 400
//   - POST payload host.hostname with special chars → 400
//   - POST valid + HMAC correct → 202
//   - POST valid + HMAC wrong (HMAC_REQUIRED=true) → 401
//   - Redis unavailable → 503
```

---

## Acceptance Criteria

- [ ] `dotnet run` khởi động được, `/health` trả 200
- [ ] `POST /api/v1/ingest` với API key hợp lệ → 202 + `{"ingestion_id": "..."}`
- [ ] Migrations chạy tự động lúc start, idempotent (chạy 2 lần không lỗi)
- [ ] `GET /health/ready` trả 503 khi Postgres hoặc Redis chưa sẵn sàng
- [ ] Rate limiter: 61 requests trong 1 phút → request thứ 61 nhận 429
- [ ] Redis XADD message có đủ fields: `ingestion_id`, `payload`, `hostname`
- [ ] Log mọi ingest request: hostname, collection_id, IP, status code
- [ ] `docker compose -f docker/compose.dev.yml up` → backend healthy trong 30s
