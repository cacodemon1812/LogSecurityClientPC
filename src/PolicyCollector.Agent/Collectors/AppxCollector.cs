using System.Diagnostics;
using System.Management.Automation;
using PolicyCollector.Agent.Infrastructure;

namespace PolicyCollector.Agent.Collectors;

public sealed class AppxCollector : ICollector<List<AppxEntry>>
{
    private readonly PowerShellRunner _ps;

    public AppxCollector(PowerShellRunner ps)
    {
        _ps = ps;
    }

    public string ModuleName => "AppxPackages";

    public async Task<CollectorResult<List<AppxEntry>>> CollectAsync(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var objects = await _ps.RunScriptAsync(
                "Get-AppxPackage -AllUsers | Select-Object Name,Version,Publisher,Architecture,InstallLocation | ConvertTo-Json -Compress",
                ct);

            var packages = ParsePackages(objects);
            return CollectorResult<List<AppxEntry>>.Ok(packages, sw.Elapsed);
        }
        catch (Exception ex)
        {
            return CollectorResult<List<AppxEntry>>.Fail("Appx collection failed", ex.ToString());
        }
    }

    private List<AppxEntry> ParsePackages(IReadOnlyList<PSObject> objects)
    {
        var packages = new List<AppxEntry>();

        foreach (var obj in objects)
        {
            if (obj?.Properties == null) continue;

            packages.Add(new AppxEntry
            {
                Name = obj.Properties["Name"]?.Value?.ToString(),
                Version = obj.Properties["Version"]?.Value?.ToString(),
                Publisher = obj.Properties["Publisher"]?.Value?.ToString(),
                Architecture = obj.Properties["Architecture"]?.Value?.ToString(),
                InstallLocation = obj.Properties["InstallLocation"]?.Value?.ToString()
            });
        }

        return packages;
    }
}
