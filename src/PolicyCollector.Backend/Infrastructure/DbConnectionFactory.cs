using Npgsql;

namespace PolicyCollector.Backend.Infrastructure;

public interface IDbConnectionFactory
{
    Task<NpgsqlConnection> OpenAsync(CancellationToken ct = default);
}

public sealed class NpgsqlConnectionFactory : IDbConnectionFactory
{
    private readonly string _connectionString;
    private readonly ILogger<NpgsqlConnectionFactory> _logger;

    public NpgsqlConnectionFactory(IConfiguration config, ILogger<NpgsqlConnectionFactory> logger)
    {
        _connectionString = config.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("Postgres connection string not found");
        _logger = logger;
    }

    public async Task<NpgsqlConnection> OpenAsync(CancellationToken ct = default)
    {
        try
        {
            var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(ct);
            return connection;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open database connection");
            throw;
        }
    }
}
