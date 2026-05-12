using System.Text.Json;
using Microsoft.Extensions.Options;
using PolicyCollector.Agent.Config;
using PolicyCollector.Agent.Transport;

namespace PolicyCollector.Agent.Jobs;

public sealed class RetryJob : BackgroundService
{
    private readonly LocalQueue _queue;
    private readonly ITransport _transport;
    private readonly LocalQueueOptions _options;
    private readonly ILogger<RetryJob> _logger;

    public RetryJob(
        LocalQueue queue,
        ITransport transport,
        IOptions<LocalQueueOptions> options,
        ILogger<RetryJob> logger)
    {
        _queue = queue;
        _transport = transport;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await Task.Delay(TimeSpan.FromSeconds(30), ct);

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(_options.RetryIntervalMinutes));

        while (await timer.WaitForNextTickAsync(ct))
        {
            var purged = _queue.PurgeExpired();
            if (purged > 0)
                _logger.LogInformation("Purged {Count} expired queue entries", purged);

            var pending = _queue.PendingCount();
            if (pending == 0)
                continue;

            _logger.LogInformation("Retrying {Count} pending payloads", pending);
            await RetryBatch(ct);
        }
    }

    private async Task RetryBatch(CancellationToken ct)
    {
        var batch = _queue.Dequeue(batchSize: 10);

        foreach (var (id, json) in batch)
        {
            if (ct.IsCancellationRequested)
                break;

            try
            {
                var payload = JsonSerializer.Deserialize<CollectionPayload>(json);
                if (payload is null)
                {
                    _queue.Remove(id);
                    continue;
                }

                var result = await _transport.SendAsync(payload, ct);
                if (result.Success)
                {
                    _queue.Remove(id);
                    _logger.LogInformation("Retry succeeded for queued payload id={Id}", id);
                }
                else if (!result.ShouldRetry)
                {
                    _queue.Remove(id);
                    _logger.LogWarning("Dropping queued payload id={Id}, non-retryable: {Error}",
                        id, result.ErrorMessage);
                }
                else
                {
                    _queue.UpdateRetry(id, result.ErrorMessage ?? "Unknown");
                }
            }
            catch (Exception ex)
            {
                _queue.UpdateRetry(id, ex.Message);
                _logger.LogWarning(ex, "Retry failed for id={Id}", id);
            }
        }
    }
}
