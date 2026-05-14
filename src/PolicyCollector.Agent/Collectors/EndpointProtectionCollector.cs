using System.Diagnostics;
using PolicyCollector.Agent.Infrastructure;

namespace PolicyCollector.Agent.Collectors;

public sealed class EndpointProtectionCollector : ICollector<EndpointProtectionResult>
{
    private readonly WmiQuery _wmi;
    private readonly RegistryReader _registry;
    private readonly ILogger<EndpointProtectionCollector> _logger;

    // Windows Security Center namespace — available on all modern Windows versions
    private const string SecurityCenterNs = @"root\SecurityCenter2";

    // Kaspersky stores all product registrations under this key
    private const string KasperskyBaseKey = @"SOFTWARE\KasperskyLab";

    public EndpointProtectionCollector(
        WmiQuery wmi,
        RegistryReader registry,
        ILogger<EndpointProtectionCollector> logger)
    {
        _wmi = wmi;
        _registry = registry;
        _logger = logger;
    }

    public string ModuleName => "EndpointProtection";

    public async Task<CollectorResult<EndpointProtectionResult>> CollectAsync(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var avTask  = _wmi.QueryAsync("AntiVirusProduct",
                properties: ["displayName", "productState", "timestamp"],
                namespacePath: SecurityCenterNs, ct: ct);

            var fwTask  = _wmi.QueryAsync("FirewallProduct",
                properties: ["displayName", "productState", "timestamp"],
                namespacePath: SecurityCenterNs, ct: ct);

            await Task.WhenAll(avTask, fwTask);

            var avProducts = avTask.Result.Select(ParseProduct).ToList();
            var fwProducts = fwTask.Result.Select(ParseProduct).ToList();

            var kasperskyAv = avProducts.FirstOrDefault(p =>
                p.Name?.Contains("Kaspersky", StringComparison.OrdinalIgnoreCase) ?? false);

            var kasperskyFw = fwProducts.FirstOrDefault(p =>
                p.Name?.Contains("Kaspersky", StringComparison.OrdinalIgnoreCase) ?? false);

            bool kasperskyDetected = kasperskyAv is not null || kasperskyFw is not null;

            KasperskyDetail? kasperskyDetail = null;
            if (kasperskyDetected)
                kasperskyDetail = BuildKasperskyDetail(kasperskyAv, kasperskyFw);

            string? firewallNote = null;
            if (!kasperskyDetected)
            {
                firewallNote = "Kaspersky not detected on this endpoint. " +
                               "Verify Windows Firewall status via the 'firewall' module " +
                               "and confirm no unauthorized third-party firewall is active.";
            }
            else if (kasperskyFw is null)
            {
                // Kaspersky AV is present but it has NOT registered a firewall product —
                // this can happen when the firewall component is disabled in the KES policy.
                firewallNote = "Kaspersky AV is installed but its firewall component is not " +
                               "registered with Windows Security Center. Check KES policy to " +
                               "ensure the Network Threat Protection component is enabled.";
            }

            var result = new EndpointProtectionResult
            {
                AntivirusProducts  = avProducts,
                FirewallProducts   = fwProducts,
                KasperskyDetected  = kasperskyDetected,
                Kaspersky          = kasperskyDetail,
                FirewallNote       = firewallNote
            };

            return CollectorResult<EndpointProtectionResult>.Ok(result, sw.Elapsed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "EndpointProtection collection failed");
            return CollectorResult<EndpointProtectionResult>.Fail("EndpointProtection collection failed", ex.ToString());
        }
    }

    // productState from SecurityCenter2 is a 24-bit integer encoded as XXYYZZ hex:
    //   XX = product category (ignored here)
    //   YY = scanner state: 10 = on/enabled, 00 = off/disabled
    //   ZZ = definition status: 00 = up to date, 10 = out of date
    private static (bool enabled, bool upToDate) DecodeProductState(object? stateObj)
    {
        uint state = stateObj switch
        {
            int  i => (uint)i,
            uint u => u,
            _      => 0
        };

        var hex = state.ToString("X6").PadLeft(6, '0');
        bool enabled   = string.Equals(hex.Substring(2, 2), "10", StringComparison.Ordinal);
        bool upToDate  = string.Equals(hex.Substring(4, 2), "00", StringComparison.Ordinal);
        return (enabled, upToDate);
    }

    private static SecurityProduct ParseProduct(IReadOnlyDictionary<string, object?> row)
    {
        row.TryGetValue("productState", out var stateObj);
        var (enabled, upToDate) = DecodeProductState(stateObj);

        uint raw = stateObj switch { int i => (uint)i, uint u => u, _ => 0 };

        return new SecurityProduct
        {
            Name      = row.TryGetValue("displayName", out var n)  ? n?.ToString() : null,
            Enabled   = enabled,
            UpToDate  = upToDate,
            StateHex  = raw.ToString("X6"),
            Timestamp = row.TryGetValue("timestamp",    out var ts) ? ts?.ToString() : null
        };
    }

    private KasperskyDetail BuildKasperskyDetail(SecurityProduct? kav, SecurityProduct? kfw)
    {
        // Walk HKLM\SOFTWARE\KasperskyLab\ to find installed product and version.
        // Subkey names follow the pattern <ProductCode><MajorVersion>.<MinorVersion>
        // e.g. AVP21.3 = Kaspersky Endpoint Security 21.3
        string? productName = null;
        string? version     = null;
        string? installPath = null;

        var productKeys = _registry.GetSubKeys(RegistryHive.LocalMachine, KasperskyBaseKey);
        foreach (var key in productKeys)
        {
            // Skip known non-product subkeys
            if (key.Equals("Shared", StringComparison.OrdinalIgnoreCase)) continue;

            var productBase = $@"{KasperskyBaseKey}\{key}";

            // Each product key has version subkeys (e.g. "3.0", "4.0")
            var versionKeys = _registry.GetSubKeys(RegistryHive.LocalMachine, productBase);
            foreach (var vk in versionKeys)
            {
                var versionPath = $@"{productBase}\{vk}";
                var path = _registry.GetString(RegistryHive.LocalMachine, versionPath, "InstallPath")
                        ?? _registry.GetString(RegistryHive.LocalMachine, versionPath, "ProductFolder");

                if (path is not null)
                {
                    productName = kav?.Name ?? kfw?.Name ?? key;
                    version     = _registry.GetString(RegistryHive.LocalMachine, versionPath, "ProductVersion") ?? vk;
                    installPath = path;
                    break;
                }
            }

            if (installPath is not null) break;
        }

        return new KasperskyDetail
        {
            ProductName        = productName,
            Version            = version,
            InstallPath        = installPath,
            AvEnabled          = kav?.Enabled     ?? false,
            AvUpToDate         = kav?.UpToDate    ?? false,
            FirewallRegistered = kfw is not null,
            FirewallEnabled    = kfw?.Enabled     ?? false
        };
    }
}
