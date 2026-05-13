using System.Diagnostics;
using Microsoft.Extensions.Options;
using PolicyCollector.Agent.Config;

namespace PolicyCollector.Agent.Jobs;

public sealed class CollectionJob
{
    private readonly IOptions<AgentOptions> _options;
    private readonly ICollector<HostInfo> _hostCollector;
    private readonly ICollector<GpoResult> _gpoCollector;
    private readonly ICollector<SecPolicyResult> _secPolicyCollector;
    private readonly ICollector<FirewallResult> _firewallCollector;
    private readonly ICollector<DefenderResult> _defenderCollector;
    private readonly ICollector<List<BitLockerVolume>> _bitLockerCollector;
    private readonly ICollector<List<AppEntry>> _appCollector;
    private readonly ICollector<List<AppxEntry>> _appxCollector;
    private readonly ICollector<List<ServiceEntry>> _serviceCollector;
    private readonly ICollector<List<TaskEntry>> _taskCollector;
    private readonly ICollector<List<StartupEntry>> _startupCollector;
    private readonly ICollector<AdInfo> _adCollector;
    private readonly ICollector<RegistryAuditResult> _registryAuditCollector;
    private readonly ILogger<CollectionJob> _logger;

    public CollectionJob(
        IOptions<AgentOptions> options,
        ICollector<HostInfo> hostCollector,
        ICollector<GpoResult> gpoCollector,
        ICollector<SecPolicyResult> secPolicyCollector,
        ICollector<FirewallResult> firewallCollector,
        ICollector<DefenderResult> defenderCollector,
        ICollector<List<BitLockerVolume>> bitLockerCollector,
        ICollector<List<AppEntry>> appCollector,
        ICollector<List<AppxEntry>> appxCollector,
        ICollector<List<ServiceEntry>> serviceCollector,
        ICollector<List<TaskEntry>> taskCollector,
        ICollector<List<StartupEntry>> startupCollector,
        ICollector<AdInfo> adCollector,
        ICollector<RegistryAuditResult> registryAuditCollector,
        ILogger<CollectionJob> logger)
    {
        _options = options;
        _hostCollector = hostCollector;
        _gpoCollector = gpoCollector;
        _secPolicyCollector = secPolicyCollector;
        _firewallCollector = firewallCollector;
        _defenderCollector = defenderCollector;
        _bitLockerCollector = bitLockerCollector;
        _appCollector = appCollector;
        _appxCollector = appxCollector;
        _serviceCollector = serviceCollector;
        _taskCollector = taskCollector;
        _startupCollector = startupCollector;
        _adCollector = adCollector;
        _registryAuditCollector = registryAuditCollector;
        _logger = logger;
    }

    public async Task<CollectionPayload> RunAsync(CancellationToken ct)
    {
        _logger.LogInformation("Collection cycle starting");
        var startTime = DateTimeOffset.UtcNow;
        var modules = _options.Value.Modules;
        var timeout = _options.Value.CollectorTimeoutSeconds;

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(timeout + 10));

        var tasks = new List<Task>();
        HostInfo? hostInfo = null;
        GpoResult? gpo = null;
        SecPolicyResult? secPolicy = null;
        FirewallResult? firewall = null;
        DefenderResult? defender = null;
        List<BitLockerVolume>? bitLocker = null;
        List<AppEntry>? applications = null;
        List<AppxEntry>? appxPackages = null;
        List<ServiceEntry>? services = null;
        List<TaskEntry>? scheduledTasks = null;
        List<StartupEntry>? startupEntries = null;
        AdInfo? activeDirectory = null;
        RegistryAuditResult? registryAudit = null;

        var hostTask = RunCollector(_hostCollector, timeout, cts.Token)
            .ContinueWith(t => hostInfo = t.Result?.Data, TaskContinuationOptions.OnlyOnRanToCompletion);
        tasks.Add(hostTask);

        if (modules.GPO)
        {
            var t = RunCollector(_gpoCollector, timeout, cts.Token)
                .ContinueWith(r => gpo = r.Result?.Data, TaskContinuationOptions.OnlyOnRanToCompletion);
            tasks.Add(t);
        }

        if (modules.SecurityPolicy)
        {
            var t = RunCollector(_secPolicyCollector, timeout, cts.Token)
                .ContinueWith(r => secPolicy = r.Result?.Data, TaskContinuationOptions.OnlyOnRanToCompletion);
            tasks.Add(t);
        }

        if (modules.Firewall)
        {
            var t = RunCollector(_firewallCollector, timeout, cts.Token)
                .ContinueWith(r => firewall = r.Result?.Data, TaskContinuationOptions.OnlyOnRanToCompletion);
            tasks.Add(t);
        }

        if (modules.Defender)
        {
            var t = RunCollector(_defenderCollector, timeout, cts.Token)
                .ContinueWith(r => defender = r.Result?.Data, TaskContinuationOptions.OnlyOnRanToCompletion);
            tasks.Add(t);
        }

        if (modules.BitLocker)
        {
            var t = RunCollector(_bitLockerCollector, timeout, cts.Token)
                .ContinueWith(r => bitLocker = r.Result?.Data, TaskContinuationOptions.OnlyOnRanToCompletion);
            tasks.Add(t);
        }

        if (modules.AppInventory)
        {
            var t = RunCollector(_appCollector, timeout, cts.Token)
                .ContinueWith(r => applications = r.Result?.Data, TaskContinuationOptions.OnlyOnRanToCompletion);
            tasks.Add(t);
        }

        if (modules.AppxPackages)
        {
            var t = RunCollector(_appxCollector, timeout, cts.Token)
                .ContinueWith(r => appxPackages = r.Result?.Data, TaskContinuationOptions.OnlyOnRanToCompletion);
            tasks.Add(t);
        }

        if (modules.Services)
        {
            var t = RunCollector(_serviceCollector, timeout, cts.Token)
                .ContinueWith(r => services = r.Result?.Data, TaskContinuationOptions.OnlyOnRanToCompletion);
            tasks.Add(t);
        }

        if (modules.ScheduledTasks)
        {
            var t = RunCollector(_taskCollector, timeout, cts.Token)
                .ContinueWith(r => scheduledTasks = r.Result?.Data, TaskContinuationOptions.OnlyOnRanToCompletion);
            tasks.Add(t);
        }

        if (modules.StartupEntries)
        {
            var t = RunCollector(_startupCollector, timeout, cts.Token)
                .ContinueWith(r => startupEntries = r.Result?.Data, TaskContinuationOptions.OnlyOnRanToCompletion);
            tasks.Add(t);
        }

        if (modules.ActiveDirectory)
        {
            var t = RunCollector(_adCollector, timeout, cts.Token)
                .ContinueWith(r => activeDirectory = r.Result?.Data, TaskContinuationOptions.OnlyOnRanToCompletion);
            tasks.Add(t);
        }

        if (modules.RegistryAudit)
        {
            var t = RunCollector(_registryAuditCollector, timeout, cts.Token)
                .ContinueWith(r => registryAudit = r.Result?.Data, TaskContinuationOptions.OnlyOnRanToCompletion);
            tasks.Add(t);
        }

        try
        {
            await Task.WhenAll(tasks);
        }
        catch
        {
        }

        var payload = PayloadBuilder.Build(
            startTime,
            hostInfo,
            gpo,
            secPolicy,
            firewall,
            defender,
            bitLocker,
            applications,
            appxPackages,
            services,
            scheduledTasks,
            startupEntries,
            activeDirectory,
            registryAudit);

        _logger.LogInformation("Collection cycle completed in {Duration}ms",
            (DateTimeOffset.UtcNow - startTime).TotalMilliseconds);

        return payload;
    }

    private async Task<CollectorResult<T>?> RunCollector<T>(
        ICollector<T> collector,
        int timeoutSeconds,
        CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        try
        {
            var result = await collector.CollectAsync(cts.Token);
            if (!result.Success)
                _logger.LogWarning("Collector {Module} failed: {Error}",
                    collector.ModuleName, result.ErrorMessage);
            return result;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Collector {Module} timed out after {Timeout}s",
                collector.ModuleName, timeoutSeconds);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Collector {Module} threw exception", collector.ModuleName);
            return null;
        }
    }
}
