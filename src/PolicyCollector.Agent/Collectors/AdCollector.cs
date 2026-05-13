using System.Diagnostics;
using System.Text.RegularExpressions;

namespace PolicyCollector.Agent.Collectors;

public sealed class AdCollector : ICollector<AdInfo>
{
    private readonly ProcessRunner _process;
    private readonly RegistryReader _registry;
    private readonly ILogger<AdCollector> _logger;

    public AdCollector(ProcessRunner process, RegistryReader registry, ILogger<AdCollector> logger)
    {
        _process = process;
        _registry = registry;
        _logger = logger;
    }

    public string ModuleName => "ActiveDirectory";

    public async Task<CollectorResult<AdInfo>> CollectAsync(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var domain = GetDomain();
            if (string.IsNullOrWhiteSpace(domain))
                return CollectorResult<AdInfo>.Ok(new AdInfo { KerberosAvailable = false }, sw.Elapsed);

            var dcInfo = await QueryDomainControllerAsync(domain, ct);

            var info = new AdInfo
            {
                DomainController  = dcInfo.DcName,
                SiteName          = dcInfo.SiteName,
                OuPath            = GetOuPath(),
                KerberosAvailable = dcInfo.DcName is not null
            };

            return CollectorResult<AdInfo>.Ok(info, sw.Elapsed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AD collection failed");
            return CollectorResult<AdInfo>.Fail("AD collection failed", ex.ToString());
        }
    }

    private string? GetDomain()
    {
        var domain = _registry.GetString(RegistryHive.LocalMachine,
            @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters", "Domain");

        if (!string.IsNullOrWhiteSpace(domain) &&
            !domain.Equals("WORKGROUP", StringComparison.OrdinalIgnoreCase))
            return domain;

        domain = _registry.GetString(RegistryHive.LocalMachine,
            @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters", "NV Domain");

        return !string.IsNullOrWhiteSpace(domain) &&
               !domain.Equals("WORKGROUP", StringComparison.OrdinalIgnoreCase)
            ? domain
            : null;
    }

    private string? GetOuPath()
    {
        // GPO processor writes the computer's Distinguished Name here after successful GPO apply
        return _registry.GetString(RegistryHive.LocalMachine,
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Group Policy\State\Machine",
            "Distinguished-Name");
    }

    private async Task<(string? DcName, string? SiteName)> QueryDomainControllerAsync(
        string domain, CancellationToken ct)
    {
        try
        {
            var result = await _process.RunAsync(
                "nltest.exe",
                $"/dsgetdc:{domain}",
                TimeSpan.FromSeconds(10),
                ct);

            if (result.ExitCode != 0)
            {
                _logger.LogWarning("nltest /dsgetdc:{Domain} exited {Code}: {Err}",
                    domain, result.ExitCode, result.Stderr.Trim());
                return (null, null);
            }

            return ParseNltestOutput(result.Stdout);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "nltest call failed");
            return (null, null);
        }
    }

    // Parses output of: nltest /dsgetdc:<domain>
    // Extracts "DC:" (domain controller name) and "Our Site Name:" (machine AD site)
    private static (string? DcName, string? SiteName) ParseNltestOutput(string output)
    {
        string? dcName   = null;
        string? siteName = null;

        foreach (var line in output.Split('\n'))
        {
            var trimmed = line.Trim();

            if (dcName is null)
            {
                var m = Regex.Match(trimmed, @"^DC:\s+\\\\(.+)$", RegexOptions.IgnoreCase);
                if (m.Success)
                    dcName = m.Groups[1].Value.Trim();
            }

            if (siteName is null)
            {
                var m = Regex.Match(trimmed, @"^Our Site Name:\s+(.+)$", RegexOptions.IgnoreCase);
                if (m.Success)
                    siteName = m.Groups[1].Value.Trim();
            }

            if (dcName is not null && siteName is not null)
                break;
        }

        return (dcName, siteName);
    }
}
