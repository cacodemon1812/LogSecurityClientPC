using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Microsoft.Win32;

namespace PolicyCollector.Agent.Collectors;

public sealed class HostInfoCollector : ICollector<HostInfo>
{
    private readonly RegistryReader _registry;
    private readonly ILogger<HostInfoCollector> _logger;

    public HostInfoCollector(RegistryReader registry, ILogger<HostInfoCollector> logger)
    {
        _registry = registry;
        _logger = logger;
    }

    public string ModuleName => "HostInfo";

    public Task<CollectorResult<HostInfo>> CollectAsync(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var info = new HostInfo
            {
                Hostname = Environment.MachineName,
                Fqdn = GetFqdn(),
                Domain = GetDomain(),
                DomainJoined = IsDomainJoined(),
                Workgroup = GetWorkgroup(),
                OsName = GetOsName(),
                OsVersion = Environment.OSVersion.Version.ToString(),
                OsBuild = GetBuildNumber(),
                OsEdition = GetEdition(),
                Architecture = Environment.Is64BitOperatingSystem ? "x64" : "x86",
                Locale = CultureInfo.CurrentCulture.Name,
                Timezone = TimeZoneInfo.Local.Id,
                LastBoot = GetLastBootTime(),
                IpAddresses = GetLocalIps()
            };
            return Task.FromResult(CollectorResult<HostInfo>.Ok(info, sw.Elapsed));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "HostInfo collection failed");
            return Task.FromResult(CollectorResult<HostInfo>.Fail("HostInfo collection failed", ex.ToString()));
        }
    }

    private string? GetDomain()
    {
        var domain = _registry.GetString(RegistryHive.LocalMachine,
            @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters", "Domain");
        if (!string.IsNullOrWhiteSpace(domain))
            return domain;

        return _registry.GetString(RegistryHive.LocalMachine,
            @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters", "NV Domain");
    }

    private string GetOsName()
    {
        var name = _registry.GetString(RegistryHive.LocalMachine,
            @"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "ProductName");
        return name ?? "Unknown";
    }

    private string GetBuildNumber()
    {
        var build = _registry.GetString(RegistryHive.LocalMachine,
            @"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "CurrentBuildNumber");
        var ubr = _registry.GetDword(RegistryHive.LocalMachine,
            @"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "UBR");

        if (ubr.HasValue)
            return $"{build}.{ubr}";

        return build ?? "Unknown";
    }

    private string? GetEdition()
    {
        return _registry.GetString(RegistryHive.LocalMachine,
            @"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "EditionID");
    }

    private bool IsDomainJoined()
    {
        var domain = GetDomain();
        return !string.IsNullOrWhiteSpace(domain) && !string.Equals(domain, "WORKGROUP", StringComparison.OrdinalIgnoreCase);
    }

    private DateTimeOffset? GetLastBootTime()
    {
        try
        {
            var uptime = Environment.TickCount64;
            return DateTimeOffset.UtcNow.AddMilliseconds(-uptime);
        }
        catch
        {
            return null;
        }
    }

    private List<string> GetLocalIps()
    {
        var ips = new List<string>();
        try
        {
            foreach (var iface in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (iface.OperationalStatus != OperationalStatus.Up)
                    continue;

                foreach (var addr in iface.GetIPProperties().UnicastAddresses)
                {
                    if (addr.Address.AddressFamily == AddressFamily.InterNetwork ||
                        addr.Address.AddressFamily == AddressFamily.InterNetworkV6)
                    {
                        ips.Add(addr.Address.ToString());
                    }
                }
            }
        }
        catch
        {
        }

        return ips;
    }

    private string GetFqdn()
    {
        try
        {
            var hostName = Dns.GetHostName();
            var entry = Dns.GetHostEntry(hostName);
            return entry.HostName ?? hostName;
        }
        catch
        {
            return Environment.MachineName;
        }
    }

    private string? GetWorkgroup()
    {
        if (IsDomainJoined())
            return null;

        try
        {
            return Environment.GetEnvironmentVariable("USERDOMAIN");
        }
        catch
        {
            return null;
        }
    }
}
