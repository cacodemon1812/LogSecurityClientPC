using System.Text.Json;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using PolicyCollector.Agent.Config;

namespace PolicyCollector.Agent.Transport;

public sealed class LocalQueue : IDisposable
{
    private readonly string _dbPath;
    private readonly LocalQueueOptions _options;
    private readonly ILogger<LocalQueue> _logger;
    private SqliteConnection? _connection;
    private readonly object _syncLock = new();

    public LocalQueue(IOptions<LocalQueueOptions> options, ILogger<LocalQueue> logger)
    {
        _dbPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "PolicyCollector", "queue.db");
        _options = options.Value;
        _logger = logger;
        EnsureInitialized();
    }

    private void EnsureInitialized()
    {
        lock (_syncLock)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_dbPath)!);
            _connection = new SqliteConnection($"Data Source={_dbPath};Mode=ReadWrite;Create=True");
            _connection.Open();

            _connection.Execute("PRAGMA journal_mode=WAL;");
            _connection.Execute("PRAGMA synchronous=NORMAL;");

            _connection.Execute("""
                CREATE TABLE IF NOT EXISTS outbox (
                    id          INTEGER PRIMARY KEY AUTOINCREMENT,
                    payload     TEXT NOT NULL,
                    created_at  TEXT NOT NULL DEFAULT (datetime('now')),
                    retry_count INTEGER NOT NULL DEFAULT 0,
                    last_error  TEXT
                )
                """);
        }
    }

    public void Enqueue(CollectionPayload payload)
    {
        lock (_syncLock)
        {
            try
            {
                var json = JsonSerializer.Serialize(payload);

                var count = _connection!.ExecuteScalar<int>("SELECT COUNT(*) FROM outbox");
                if (count >= _options.MaxEntries)
                {
                    var toDrop = count - _options.MaxEntries + 1;
                    _connection!.Execute(
                        $"DELETE FROM outbox WHERE id IN (SELECT id FROM outbox ORDER BY id ASC LIMIT {toDrop})");
                    _logger.LogWarning("LocalQueue full, dropped {Count} oldest entries", toDrop);
                }

                _connection!.Execute(
                    "INSERT INTO outbox (payload) VALUES (@payload)",
                    new { payload = json });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to enqueue payload");
                throw;
            }
        }
    }

    public IReadOnlyList<(int Id, string PayloadJson)> Dequeue(int batchSize = 10)
    {
        lock (_syncLock)
        {
            try
            {
                return _connection!
                    .Query<(int Id, string PayloadJson)>(
                        "SELECT id, payload FROM outbox ORDER BY id ASC LIMIT @batchSize",
                        new { batchSize })
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to dequeue payloads");
                return new List<(int, string)>();
            }
        }
    }

    public void Remove(int id)
    {
        lock (_syncLock)
        {
            try
            {
                _connection!.Execute("DELETE FROM outbox WHERE id = @id", new { id });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to remove queued payload");
            }
        }
    }

    public void UpdateRetry(int id, string error)
    {
        lock (_syncLock)
        {
            try
            {
                _connection!.Execute(
                    "UPDATE outbox SET retry_count = retry_count + 1, last_error = @error WHERE id = @id",
                    new { id, error });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update retry count");
            }
        }
    }

    public int PurgeExpired()
    {
        lock (_syncLock)
        {
            try
            {
                var cutoff = DateTime.UtcNow.AddHours(-_options.MaxAgeHours).ToString("yyyy-MM-ddTHH:mm:ssZ");
                return _connection!.Execute(
                    "DELETE FROM outbox WHERE created_at < @cutoff", new { cutoff });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to purge expired entries");
                return 0;
            }
        }
    }

    public int PendingCount()
    {
        lock (_syncLock)
        {
            try
            {
                return _connection!.ExecuteScalar<int>("SELECT COUNT(*) FROM outbox");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get pending count");
                return 0;
            }
        }
    }

    public void Dispose()
    {
        lock (_syncLock)
        {
            _connection?.Dispose();
        }
    }
}
