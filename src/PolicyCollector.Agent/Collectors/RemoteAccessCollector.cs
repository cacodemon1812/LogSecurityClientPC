using System.Diagnostics;
using PolicyCollector.Agent.Infrastructure;

namespace PolicyCollector.Agent.Collectors;

public sealed class RemoteAccessCollector : ICollector<RemoteAccessResult>
{
    private readonly RegistryReader _registry;
    private readonly WmiQuery _wmi;
    private readonly ILogger<RemoteAccessCollector> _logger;

    private const string WinRmServiceKey    = @"SYSTEM\CurrentControlSet\Services\WinRM";
    private const string WinRmClientKey     = @"SOFTWARE\Policies\Microsoft\Windows\WinRM\Client";
    private const string WinRmServicePolKey = @"SOFTWARE\Policies\Microsoft\Windows\WinRM\Service";
    private const string WinRmShellKey      = @"SOFTWARE\Policies\Microsoft\Windows\WinRM\Service\WinRS";
    private const string OpenSshServerKey   = @"SOFTWARE\OpenSSH";
    private const string TelnetKey          = @"SYSTEM\CurrentControlSet\Services\TlntSvr";

    public RemoteAccessCollector(RegistryReader registry, WmiQuery wmi,
        ILogger<RemoteAccessCollector> logger)
    {
        _registry = registry;
        _wmi = wmi;
        _logger = logger;
    }

    public string ModuleName => "RemoteAccess";

    public async Task<CollectorResult<RemoteAccessResult>> CollectAsync(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var serviceNames = new[] { "WinRM", "sshd", "TlntSvr" };
            var servicesTask = _wmi.QueryAsync(
                "Win32_Service",
                condition: $"Name='{serviceNames[0]}' OR Name='{serviceNames[1]}' OR Name='{serviceNames[2]}'",
                properties: ["Name", "State"],
                ct: ct);

            var serviceRows = await servicesTask;
            var statusMap = serviceRows.ToDictionary(
                r => r.TryGetValue("Name", out var n) ? n?.ToString() ?? "" : "",
                r => r.TryGetValue("State", out var s) ? s?.ToString() : null,
                StringComparer.OrdinalIgnoreCase);

            // WinRM
            var winRm = new WinRmSettings
            {
                ServiceStatus          = statusMap.GetValueOrDefault("WinRM"),
                AllowBasicAuth         = _registry.GetDword(RegistryHive.LocalMachine, WinRmClientKey, "AllowBasic") == 1
                                      || _registry.GetDword(RegistryHive.LocalMachine, WinRmServicePolKey, "AllowBasic") == 1,
                AllowUnencrypted       = _registry.GetDword(RegistryHive.LocalMachine, WinRmClientKey, "AllowUnencrypted") == 1
                                      || _registry.GetDword(RegistryHive.LocalMachine, WinRmServicePolKey, "AllowUnencrypted") == 1,
                AllowRemoteShellAccess = _registry.GetDword(RegistryHive.LocalMachine, WinRmShellKey, "AllowRemoteShellAccess") != 0
            };

            // OpenSSH
            var sshInstalled = _registry.KeyExists(RegistryHive.LocalMachine, OpenSshServerKey)
                            || statusMap.ContainsKey("sshd");
            var openSsh = new OpenSshSettings
            {
                Installed     = sshInstalled,
                ServiceStatus = statusMap.GetValueOrDefault("sshd"),
                DefaultShell  = _registry.GetString(RegistryHive.LocalMachine, OpenSshServerKey, "DefaultShell")
            };

            // Telnet
            var telnetStart  = _registry.GetDword(RegistryHive.LocalMachine, TelnetKey, "Start") ?? 4;
            var telnetStatus = statusMap.GetValueOrDefault("TlntSvr");
            var telnetEnabled = telnetStart < 4 || string.Equals(telnetStatus, "Running", StringComparison.OrdinalIgnoreCase);

            return CollectorResult<RemoteAccessResult>.Ok(
                new RemoteAccessResult { WinRm = winRm, OpenSsh = openSsh, TelnetServer = telnetEnabled },
                sw.Elapsed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RemoteAccess collection failed");
            return CollectorResult<RemoteAccessResult>.Fail("RemoteAccess collection failed", ex.ToString());
        }
    }
}
