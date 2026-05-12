using System.Diagnostics;
using PolicyCollector.Agent.Infrastructure;

namespace PolicyCollector.Agent.Collectors;

public sealed class AppInventoryCollector : ICollector<List<AppEntry>>
{
    private readonly RegistryReader _registry;

    public AppInventoryCollector(RegistryReader registry)
    {
        _registry = registry;
    }

    public string ModuleName => "AppInventory";

    public async Task<CollectorResult<List<AppEntry>>> CollectAsync(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var apps = await Task.Run(() => ReadAllApps(), ct);
            return CollectorResult<List<AppEntry>>.Ok(apps, sw.Elapsed);
        }
        catch (Exception ex)
        {
            return CollectorResult<List<AppEntry>>.Fail("AppInventory collection failed", ex.ToString());
        }
    }

    private List<AppEntry> ReadAllApps()
    {
        var results = new List<AppEntry>();

        ReadFromKey(RegistryHive.LocalMachine,
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", "x64", results);

        ReadFromKey(RegistryHive.LocalMachine,
            @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall", "x86", results);

        return results
            .Where(a => !string.IsNullOrWhiteSpace(a.DisplayName))
            .OrderBy(a => a.DisplayName)
            .ToList();
    }

    private void ReadFromKey(RegistryHive hive, string keyPath, string arch, List<AppEntry> results)
    {
        foreach (var subKey in _registry.GetSubKeys(hive, keyPath))
        {
            var fullPath = $@"{keyPath}\{subKey}";
            var name = _registry.GetString(hive, fullPath, "DisplayName");
            if (string.IsNullOrWhiteSpace(name)) continue;

            var uninstallString = _registry.GetString(hive, fullPath, "UninstallString");

            results.Add(new AppEntry
            {
                DisplayName = name,
                DisplayVersion = _registry.GetString(hive, fullPath, "DisplayVersion"),
                Publisher = _registry.GetString(hive, fullPath, "Publisher"),
                InstallDate = _registry.GetString(hive, fullPath, "InstallDate"),
                InstallLocation = _registry.GetString(hive, fullPath, "InstallLocation"),
                UninstallString = uninstallString,
                Architecture = arch,
                Source = DetermineSource(uninstallString),
                RegistryHive = hive == RegistryHive.LocalMachine ? "HKLM" : "HKCU",
                RegistryKey = fullPath
            });
        }
    }

    private static string DetermineSource(string? uninstallString)
    {
        if (string.IsNullOrEmpty(uninstallString))
            return "Unknown";

        if (uninstallString.Contains("MsiExec.exe", StringComparison.OrdinalIgnoreCase))
            return "MSI";

        return "EXE";
    }
}
