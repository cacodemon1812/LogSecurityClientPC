using System.Diagnostics;
using PolicyCollector.Agent.Infrastructure;

namespace PolicyCollector.Agent.Collectors;

public sealed class EventLogSettingsCollector : ICollector<EventLogSettings>
{
    private readonly PowerShellRunner _ps;
    private readonly RegistryReader _registry;
    private readonly ILogger<EventLogSettingsCollector> _logger;

    private static readonly string[] TargetLogs =
    [
        "Security", "System", "Application",
        "Microsoft-Windows-PowerShell/Operational",
        "Microsoft-Windows-Sysmon/Operational",
        "Microsoft-Windows-TaskScheduler/Operational",
        "Microsoft-Windows-TerminalServices-LocalSessionManager/Operational",
        "Microsoft-Windows-Windows Defender/Operational"
    ];

    private const string WefKey = @"SOFTWARE\Policies\Microsoft\Windows\EventLog\EventForwarding\SubscriptionManager";

    public EventLogSettingsCollector(PowerShellRunner ps, RegistryReader registry,
        ILogger<EventLogSettingsCollector> logger)
    {
        _ps = ps;
        _registry = registry;
        _logger = logger;
    }

    public string ModuleName => "EventLogSettings";

    public async Task<CollectorResult<EventLogSettings>> CollectAsync(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            // Build quoted list for the PS filter
            var nameFilter = string.Join(", ", TargetLogs.Select(l => $"'{l}'"));
            var script = $$"""
                $names = @({{nameFilter}})
                Get-WinEvent -ListLog * -ErrorAction SilentlyContinue |
                  Where-Object { $names -contains $_.LogName } |
                  Select-Object LogName, IsEnabled, MaximumSizeInBytes, LogMode, RecordCount
                """;

            var objects = await _ps.RunScriptAsync(script, ct);

            var logs = objects.Select(obj => new EventLogInfo
            {
                Name        = obj.Properties["LogName"]?.Value?.ToString(),
                Enabled     = obj.Properties["IsEnabled"]?.Value is true,
                MaxSizeMb   = Convert.ToInt64(obj.Properties["MaximumSizeInBytes"]?.Value ?? 0L) / (1024 * 1024),
                LogMode     = obj.Properties["LogMode"]?.Value?.ToString(),
                RecordCount = Convert.ToInt64(obj.Properties["RecordCount"]?.Value ?? 0L)
            }).ToList();

            // WEF: any value under the SubscriptionManager key means forwarding is configured
            var wefValues = _registry.GetAllValues(RegistryHive.LocalMachine, WefKey);
            var eventForwardingEnabled = wefValues is { Count: > 0 };

            return CollectorResult<EventLogSettings>.Ok(
                new EventLogSettings { Logs = logs, EventForwardingEnabled = eventForwardingEnabled },
                sw.Elapsed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "EventLogSettings collection failed");
            return CollectorResult<EventLogSettings>.Fail("EventLogSettings collection failed", ex.ToString());
        }
    }
}
