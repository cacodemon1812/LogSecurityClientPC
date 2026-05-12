using System.Diagnostics;
using PolicyCollector.Agent.Infrastructure;

namespace PolicyCollector.Agent.Collectors;

public sealed class StartupCollector : ICollector<List<StartupEntry>>
{
    private readonly RegistryReader _registry;

    public StartupCollector(RegistryReader registry)
    {
        _registry = registry;
    }

    public string ModuleName => "StartupEntries";

    public async Task<CollectorResult<List<StartupEntry>>> CollectAsync(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var entries = await Task.Run(() =>
            {
                var result = new List<StartupEntry>();

                ReadRunKey(RegistryHive.LocalMachine,
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", result);

                ReadRunKey(RegistryHive.LocalMachine,
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce", result);

                ReadRunKey(RegistryHive.LocalMachine,
                    @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run", result);

                return result;
            }, ct);

            return CollectorResult<List<StartupEntry>>.Ok(entries, sw.Elapsed);
        }
        catch (Exception ex)
        {
            return CollectorResult<List<StartupEntry>>.Fail("Startup collection failed", ex.ToString());
        }
    }

    private void ReadRunKey(RegistryHive hive, string keyPath, List<StartupEntry> result)
    {
        var values = _registry.GetAllValues(hive, keyPath);
        foreach (var (name, value) in values)
        {
            result.Add(new StartupEntry
            {
                Name = name,
                Command = value?.ToString(),
                Location = keyPath,
                Enabled = true
            });
        }
    }
}
