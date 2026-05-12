using Dapper;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

namespace PolicyCollector.Backend.Infrastructure;

public sealed class DatabaseHealthCheck : IHealthCheck
{
    private readonly IDbConnectionFactory _db;

    public DatabaseHealthCheck(IDbConnectionFactory db)
    {
        _db = db;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken ct)
    {
        try
        {
            await using var conn = await _db.OpenAsync(ct);
            await conn.ExecuteScalarAsync("SELECT 1");
            return HealthCheckResult.Healthy("PostgreSQL connection OK");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("PostgreSQL connection failed", ex);
        }
    }
}

public sealed class RedisHealthCheck : IHealthCheck
{
    private readonly IConnectionMultiplexer _redis;

    public RedisHealthCheck(IConnectionMultiplexer redis)
    {
        _redis = redis;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken ct)
    {
        try
        {
            var db = _redis.GetDatabase();
            await db.PingAsync();
            return HealthCheckResult.Healthy("Redis connection OK");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Redis connection failed", ex);
        }
    }
}
