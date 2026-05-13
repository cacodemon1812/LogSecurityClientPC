using System.Diagnostics;
using PolicyCollector.Agent.Infrastructure;

namespace PolicyCollector.Agent.Collectors;

public sealed class PatchCollector : ICollector<PatchStatus>
{
    private readonly RegistryReader _registry;
    private readonly WmiQuery _wmi;
    private readonly ILogger<PatchCollector> _logger;

    private const string WuKey     = @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate";
    private const string WuAuKey   = @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU";
    private const string WuReportKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\Results";

    public PatchCollector(RegistryReader registry, WmiQuery wmi, ILogger<PatchCollector> logger)
    {
        _registry = registry;
        _wmi = wmi;
        _logger = logger;
    }

    public string ModuleName => "Patch";

    public async Task<CollectorResult<PatchStatus>> CollectAsync(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var hotfixesTask = _wmi.QueryAsync(
                "Win32_QuickFixEngineering",
                properties: ["HotFixID", "Description", "InstalledOn"],
                ct: ct);

            var autoUpdateOptions = _registry.GetDword(RegistryHive.LocalMachine, WuAuKey, "AUOptions") ?? 0;
            var noAutoUpdate      = _registry.GetDword(RegistryHive.LocalMachine, WuAuKey, "NoAutoUpdate") == 1;
            var wsusServer        = _registry.GetString(RegistryHive.LocalMachine, WuKey, "WUServer");

            var lastInstall = _registry.GetString(RegistryHive.LocalMachine,
                $@"{WuReportKey}\Install", "LastSuccessTime");
            var lastDetect  = _registry.GetString(RegistryHive.LocalMachine,
                $@"{WuReportKey}\Detect", "LastSuccessTime");

            var wmiRows = await hotfixesTask;
            var hotfixes = wmiRows
                .Select(r => new HotfixEntry
                {
                    HotfixId    = r.TryGetValue("HotFixID", out var hid) ? hid?.ToString() : null,
                    Description = r.TryGetValue("Description", out var desc) ? desc?.ToString() : null,
                    InstalledOn = r.TryGetValue("InstalledOn", out var ins) ? ins?.ToString() : null
                })
                .ToList();

            var result = new PatchStatus
            {
                AutoUpdateOptions  = (int)autoUpdateOptions,
                NoAutoUpdate       = noAutoUpdate,
                WsusServer         = wsusServer,
                LastSuccessInstall = lastInstall,
                LastSuccessDetect  = lastDetect,
                HotfixCount        = hotfixes.Count,
                Hotfixes           = hotfixes
            };

            return CollectorResult<PatchStatus>.Ok(result, sw.Elapsed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Patch collection failed");
            return CollectorResult<PatchStatus>.Fail("Patch collection failed", ex.ToString());
        }
    }
}
