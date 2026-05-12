using System.Diagnostics;
using PolicyCollector.Agent.Infrastructure;

namespace PolicyCollector.Agent.Collectors;

public sealed class ServiceCollector : ICollector<List<ServiceEntry>>
{
    private readonly WmiQuery _wmi;
    private readonly RegistryReader _registry;
    private readonly ILogger<ServiceCollector> _logger;

    public ServiceCollector(WmiQuery wmi, RegistryReader registry, ILogger<ServiceCollector> logger)
    {
        _wmi = wmi;
        _registry = registry;
        _logger = logger;
    }

    public string ModuleName => "Services";

    public async Task<CollectorResult<List<ServiceEntry>>> CollectAsync(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var rows = await _wmi.QueryAsync(
                "Win32_Service",
                properties: new[] { "Name", "DisplayName", "State", "StartMode", "StartName", "PathName", "Description", "ProcessId" },
                ct: ct);

            var services = new List<ServiceEntry>();
            foreach (var row in rows)
            {
                var name = row.TryGetValue("Name", out var nameObj) ? nameObj?.ToString() : null;
                if (string.IsNullOrEmpty(name)) continue;

                var startMode = row.TryGetValue("StartMode", out var smObj) ? smObj?.ToString() : null;

                services.Add(new ServiceEntry
                {
                    Name = name,
                    DisplayName = row.TryGetValue("DisplayName", out var dnObj) ? dnObj?.ToString() : null,
                    Status = row.TryGetValue("State", out var stateObj) ? stateObj?.ToString() ?? "Unknown" : "Unknown",
                    StartupType = MapStartMode(startMode),
                    Account = row.TryGetValue("StartName", out var snObj) ? snObj?.ToString() : null,
                    BinaryPath = row.TryGetValue("PathName", out var pnObj) ? pnObj?.ToString() : null,
                    Description = row.TryGetValue("Description", out var descObj) ? descObj?.ToString() : null,
                    Pid = row.TryGetValue("ProcessId", out var pidObj) && pidObj is int pid ? pid : null
                });
            }

            return CollectorResult<List<ServiceEntry>>.Ok(
                services.OrderBy(s => s.Name).ToList(),
                sw.Elapsed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Services collection failed");
            return CollectorResult<List<ServiceEntry>>.Fail("Services collection failed", ex.ToString());
        }
    }

    private string MapStartMode(string? startMode)
    {
        if (string.IsNullOrEmpty(startMode))
            return "Unknown";

        if (startMode.Equals("Auto", StringComparison.OrdinalIgnoreCase))
            return "Automatic";

        if (startMode.Equals("Manual", StringComparison.OrdinalIgnoreCase))
            return "Manual";

        if (startMode.Equals("Disabled", StringComparison.OrdinalIgnoreCase))
            return "Disabled";

        return startMode;
    }
}
