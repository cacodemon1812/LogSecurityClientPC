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

            if (!result.Success)
            {
                LogSendFailure(result, payload);
                if (result.ShouldRetry)
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

    private void LogSendFailure(TransportResult result, CollectionPayload payload)
    {
        if (result.HttpStatusCode.HasValue)
            _logger.LogWarning(
                "Backend rejected payload — HTTP {StatusCode}: {Error} (will retry={ShouldRetry})",
                result.HttpStatusCode, result.ErrorMessage, result.ShouldRetry);
        else
            _logger.LogWarning(
                "Backend unreachable — {Error} (will retry={ShouldRetry})",
                result.ErrorMessage, result.ShouldRetry);

        var host = payload.Host;
        var modules = new List<string>();
        if (payload.Gpo is not null) modules.Add("GPO");
        if (payload.SecurityPolicy is not null) modules.Add("SecurityPolicy");
        if (payload.Firewall is not null) modules.Add("Firewall");
        if (payload.Defender is not null) modules.Add("Defender");
        if (payload.BitLocker is not null) modules.Add($"BitLocker({payload.BitLocker.Count})");
        if (payload.Applications is not null) modules.Add($"Apps({payload.Applications.Count})");
        if (payload.AppxPackages is not null) modules.Add($"AppxPackages({payload.AppxPackages.Count})");
        if (payload.Services is not null) modules.Add($"Services({payload.Services.Count})");
        if (payload.ScheduledTasks is not null) modules.Add($"Tasks({payload.ScheduledTasks.Count})");
        if (payload.StartupEntries is not null) modules.Add($"Startup({payload.StartupEntries.Count})");

        _logger.LogInformation(
            "Collected data summary (not yet delivered) — id={CollectionId} at={CollectedAt} " +
            "host={Hostname} ips=[{Ips}] os={OsName} {OsVersion} modules=[{Modules}]",
            payload.CollectionId,
            payload.CollectedAt,
            host?.Hostname ?? "(unknown)",
            host is not null ? string.Join(", ", host.IpAddresses) : "",
            host?.OsName ?? "",
            host?.OsVersion ?? "",
            modules.Count > 0 ? string.Join(", ", modules) : "none");
    }
}
