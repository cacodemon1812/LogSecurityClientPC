using System.Text.Json;
using StackExchange.Redis;
using PolicyCollector.Backend.Data.Repositories;
using PolicyCollector.Backend.Services;

namespace PolicyCollector.Backend.Workers;

public sealed class StorageWorker : BackgroundService
{
    private const string StreamKey = "ingest:queue";
    private const string GroupName = "storage-workers";
    private readonly string _consumerName = $"storage-{Environment.MachineName}-{Guid.NewGuid():N}";

    private readonly RedisQueue _queue;
    private readonly SnapshotRepository _snapshots;
    private readonly HostRepository _hosts;
    private readonly ConfigChangeRepository _changes;
    private readonly DiffService _diff;
    private readonly ILogger<StorageWorker> _logger;

    public StorageWorker(
        RedisQueue queue,
        SnapshotRepository snapshots,
        HostRepository hosts,
        ConfigChangeRepository changes,
        DiffService diff,
        ILogger<StorageWorker> logger)
    {
        _queue = queue;
        _snapshots = snapshots;
        _hosts = hosts;
        _changes = changes;
        _diff = diff;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _logger.LogInformation("StorageWorker started, consumer={Consumer}", _consumerName);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var entries = await _queue.ReadGroupAsync(StreamKey, GroupName, _consumerName, count: 10);

                if (entries.Count == 0)
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), ct);
                    continue;
                }

                foreach (var entry in entries)
                {
                    await ProcessEntry(entry, ct);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
            catch (Exception ex)
            {
                _logger.LogError(ex, "StorageWorker error, will retry in 5s");
                await Task.Delay(TimeSpan.FromSeconds(5), ct);
            }
        }
    }

    private async Task ProcessEntry(StreamEntry entry, CancellationToken ct)
    {
        var messageId = entry.Id;
        string? json = null;
        try
        {
            json = entry.Values.FirstOrDefault(v => v.Name == "payload").Value;
            if (string.IsNullOrEmpty(json))
            {
                _logger.LogWarning("Empty payload in message {Id}", messageId);
                await _queue.AcknowledgeAsync(StreamKey, GroupName, messageId);
                return;
            }

            var payload = JsonSerializer.Deserialize<CollectionPayload>(json);
            if (payload is null)
            {
                _logger.LogWarning("Deserialize returned null for message {Id}", messageId);
                await _queue.AcknowledgeAsync(StreamKey, GroupName, messageId);
                return;
            }

            await SaveWithRetry(payload, ct);
            await _queue.AcknowledgeAsync(StreamKey, GroupName, messageId);

            _logger.LogDebug("Stored snapshot for {Hostname}, collection_id={CollectionId}",
                payload.Host?.Hostname, payload.CollectionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process message {Id}", messageId);
        }
    }

    private async Task SaveWithRetry(CollectionPayload payload, CancellationToken ct)
    {
        var retries = 0;
        while (true)
        {
            try
            {
                await using var conn = await _snapshots.OpenConnectionAsync(ct);
                await using var tx = await conn.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, ct);

                var snapshotId = await _snapshots.UpsertAsync(conn, tx, payload, ct);
                await _hosts.UpsertAsync(conn, tx, payload, snapshotId, ct);

                var previous = await _snapshots.GetPreviousAsync(conn, payload.Host!.Hostname, snapshotId, ct);
                if (previous is not null)
                {
                    var configChanges = _diff.ComputeDiff(previous, payload);
                    if (configChanges.Count > 0)
                        await _changes.InsertBatchAsync(conn, tx, configChanges, snapshotId, ct);
                }

                await tx.CommitAsync(ct);
                return;
            }
            catch (Exception ex) when (retries < 3)
            {
                retries++;
                _logger.LogWarning(ex, "DB error saving snapshot, retry {Attempt}/3", retries);
                await Task.Delay(TimeSpan.FromSeconds(retries * 2), ct);
            }
        }
    }
}
