using Dapper;
using Microsoft.AspNetCore.RateLimiting;
using PolicyCollector.Backend.Api.Endpoints;
using PolicyCollector.Backend.Api.Middleware;
using PolicyCollector.Backend.Config;
using PolicyCollector.Backend.Data;
using PolicyCollector.Backend.Data.Repositories;
using PolicyCollector.Backend.Infrastructure;
using PolicyCollector.Backend.Services;
using PolicyCollector.Backend.Workers;
using Serilog;
using StackExchange.Redis;

// Map snake_case DB columns → PascalCase C# properties globally
DefaultTypeMap.MatchNamesWithUnderscores = true;

var builder = WebApplication.CreateBuilder(args);

// WORKER_TYPE env: "storage" | "alert" → worker-only mode (no API, no migrations)
// unset or any other value → full mode (API + both workers + migrations)
var workerType = (builder.Configuration["WORKER_TYPE"] ?? "").ToLowerInvariant();
var isWorkerOnly = workerType is "storage" or "alert";

// Serilog
builder.Host.UseSerilog((ctx, cfg) => cfg.ReadFrom.Configuration(ctx.Configuration));

// Config
builder.Services.Configure<BackendOptions>(builder.Configuration.GetSection("Backend"));

// Database
builder.Services.AddSingleton<IDbConnectionFactory, NpgsqlConnectionFactory>();

// Redis
builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
{
    var connStr = builder.Configuration.GetConnectionString("Redis")
        ?? throw new InvalidOperationException("Redis connection string not found");
    return ConnectionMultiplexer.Connect(connStr);
});
builder.Services.AddSingleton<RedisQueue>();

// Repositories
builder.Services.AddSingleton<SnapshotRepository>();
builder.Services.AddSingleton<HostRepository>();
builder.Services.AddSingleton<ConfigChangeRepository>();
builder.Services.AddSingleton<ViolationRepository>();
builder.Services.AddSingleton<PolicyRuleRepository>();
builder.Services.AddSingleton<AppInventoryRepository>();

// Services
builder.Services.AddSingleton<DiffService>();
builder.Services.AddSingleton<ViolationEngine>();
builder.Services.AddSingleton<ComplianceReportService>();
builder.Services.AddHttpClient<AlertSender>();
builder.Services.AddMemoryCache();

if (!isWorkerOnly)
{
    builder.Services.AddRateLimiter(options =>
    {
        options.AddFixedWindowLimiter("ingest", cfg =>
        {
            cfg.PermitLimit = 60;
            cfg.Window = TimeSpan.FromMinutes(1);
            cfg.QueueLimit = 0;
        });
    });

    builder.Services.AddHealthChecks()
        .AddCheck<DatabaseHealthCheck>("database")
        .AddCheck<RedisHealthCheck>("redis");
}

// Background workers — registered selectively based on WORKER_TYPE
if (!isWorkerOnly || workerType == "storage")
    builder.Services.AddHostedService<StorageWorker>();
if (!isWorkerOnly || workerType == "alert")
    builder.Services.AddHostedService<AlertWorker>();

var app = builder.Build();

if (!isWorkerOnly)
{
    // Run DB migrations (only in API/full mode — workers wait for backend healthcheck)
    try
    {
        var connStr = app.Configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("Postgres connection string not found");
        await MigrationRunner.RunAsync(connStr);
    }
    catch (Exception ex)
    {
        app.Logger.LogCritical(ex, "Database migration failed — startup aborted");
        throw new InvalidOperationException("Database migration failed", ex);
    }

    app.UseMiddleware<RequestLoggingMiddleware>();
    app.UseMiddleware<ApiKeyMiddleware>();
    app.UseMiddleware<HmacValidationMiddleware>();
    app.UseRateLimiter();

    app.MapIngestEndpoint();
    app.MapHostsEndpoints();
    app.MapDiffEndpoints();
    app.MapViolationsEndpoints();
    app.MapInventoryEndpoints();
    app.MapReportEndpoints();
    app.MapAdminEndpoints();
    app.MapHealthChecks("/health");
    app.MapHealthChecks("/health/ready");
    app.MapHealthChecks("/health/live");
}

await app.RunAsync();
