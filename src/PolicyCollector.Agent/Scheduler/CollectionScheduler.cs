using Microsoft.Extensions.Options;
using PolicyCollector.Agent.Config;
using PolicyCollector.Agent.Jobs;
using PolicyCollector.Agent.Transport;

namespace PolicyCollector.Agent.Scheduler;

public sealed class CollectionScheduler : BackgroundService
{
    private readonly CollectionJob _job;
    private readonly ITransport _transport;
    private readonly LocalQueue _queue;
    private readonly AgentOptions _options;
    private readonly ILogger<CollectionScheduler> _logger;

    public CollectionScheduler(
        CollectionJob job,
        ITransport transport,
        LocalQueue queue,
        IOptions<AgentOptions> options,
        ILogger<CollectionScheduler> logger)
    {
        _job = job;
        _transport = transport;
        _queue = queue;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _logger.LogInformation("PolicyCollector scheduler started, interval={Interval}min",
            _options.IntervalMinutes);

        if (_options.CollectOnStartup)
        {
            _logger.LogInformation("Running initial collection on startup");
            await RunCollectionCycle(ct);
        }

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(_options.IntervalMinutes));

        while (await timer.WaitForNextTickAsync(ct))
        {
            await RunCollectionCycle(ct);
        }
    }

    private async Task RunCollectionCycle(CancellationToken ct)
    {
        try
        {
            var payload = await _job.RunAsync(ct);
            var result = await _transport.SendAsync(payload, ct);

            if (!result.Success && result.ShouldRetry)
            {
                _logger.LogWarning("Send failed (retry), queuing payload locally");
                _queue.Enqueue(payload);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Collection cycle failed unexpectedly");
        }
    }
}
