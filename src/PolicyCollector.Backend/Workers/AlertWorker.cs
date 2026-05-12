using System.Text.Json;
using StackExchange.Redis;
using PolicyCollector.Backend.Data.Repositories;
using PolicyCollector.Backend.Services;

namespace PolicyCollector.Backend.Workers;

public sealed class AlertWorker : BackgroundService
{
    private const string StreamKey = "ingest:queue";
    private const string GroupName = "alert-workers";
    private readonly string _consumerName = $"alert-{Environment.MachineName}-{Guid.NewGuid():N}";

    private readonly RedisQueue _queue;
    private readonly ViolationEngine _engine;
    private readonly ViolationRepository _violations;
    private readonly AlertSender _alertSender;
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<AlertWorker> _logger;

    public AlertWorker(
        RedisQueue queue,
        ViolationEngine engine,
        ViolationRepository violations,
        AlertSender alertSender,
        IConnectionMultiplexer redis,
        ILogger<AlertWorker> logger)
    {
        _queue = queue;
        _engine = engine;
        _violations = violations;
        _alertSender = alertSender;
        _redis = redis;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _logger.LogInformation("AlertWorker started, consumer={Consumer}", _consumerName);

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
                    await ProcessAlert(entry, ct);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AlertWorker error, will retry in 5s");
                await Task.Delay(TimeSpan.FromSeconds(5), ct);
            }
        }
    }

    private async Task ProcessAlert(StreamEntry entry, CancellationToken ct)
    {
        try
        {
            var json = entry.Values.FirstOrDefault(v => v.Name == "payload").Value.ToString();
            if (string.IsNullOrEmpty(json))
            {
                await _queue.AcknowledgeAsync(StreamKey, GroupName, entry.Id);
                return;
            }

            var payload = JsonSerializer.Deserialize<CollectionPayload>(json);
            if (payload is null)
            {
                await _queue.AcknowledgeAsync(StreamKey, GroupName, entry.Id);
                return;
            }

            // Get the snapshot ID from ingestion_id to correlate violations
            var ingestionId = entry.Values.FirstOrDefault(v => v.Name == "ingestion_id").Value.ToString();

            var violations = await _engine.EvaluateAsync(payload, ct);

            if (violations.Count > 0)
            {
                // For now, use a placeholder Guid — in production would retrieve actual snapshot_id
                var snapshotId = Guid.Empty;
                await _violations.InsertNewAsync(snapshotId, violations, ct);

                var urgent = violations
                    .Where(v => v.Severity is "critical" or "high")
                    .ToList();

                if (urgent.Count > 0)
                {
                    await _alertSender.SendAsync(payload.Host?.Hostname ?? "unknown", urgent, ct);
                }

                _logger.LogInformation("Detected {Count} violations for {Host} ({Urgent} urgent)",
                    violations.Count, payload.Host?.Hostname, urgent.Count);
            }

            await _queue.AcknowledgeAsync(StreamKey, GroupName, entry.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Alert processing failed for message {Id}", entry.Id);
        }
    }
}
