using System.Diagnostics;
using PolicyCollector.Agent.Infrastructure;

namespace PolicyCollector.Agent.Collectors;

public sealed class HardwareSecurityCollector : ICollector<HardwareSecurity>
{
    private readonly PowerShellRunner _ps;
    private readonly WmiQuery _wmi;
    private readonly RegistryReader _registry;
    private readonly ILogger<HardwareSecurityCollector> _logger;

    private const string DevGuardKey  = @"SYSTEM\CurrentControlSet\Control\DeviceGuard";
    private const string VbsKey       = @"SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity";
    private const string HvciKey      = VbsKey;

    public HardwareSecurityCollector(
        PowerShellRunner ps, WmiQuery wmi, RegistryReader registry,
        ILogger<HardwareSecurityCollector> logger)
    {
        _ps = ps;
        _wmi = wmi;
        _registry = registry;
        _logger = logger;
    }

    public string ModuleName => "HardwareSecurity";

    public async Task<CollectorResult<HardwareSecurity>> CollectAsync(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var secureBootTask = _ps.RunScriptAsync(
                "try { Confirm-SecureBootUEFI -ErrorAction Stop } catch { $null }",
                ct);

            var tpmTask = _wmi.QueryAsync(
                "Win32_Tpm",
                properties: ["IsPresent", "IsEnabled_InitialValue", "IsActivated_InitialValue", "SpecVersion"],
                namespacePath: @"root\cimv2\Security\MicrosoftTpm",
                ct: ct);

            await Task.WhenAll(secureBootTask, tpmTask);

            bool? secureBootEnabled = null;
            var sbObj = secureBootTask.Result.FirstOrDefault();
            if (sbObj?.BaseObject is bool sbVal)
                secureBootEnabled = sbVal;

            bool tpmPresent = false, tpmEnabled = false, tpmActivated = false;
            string? tpmVersion = null;
            var tpmRow = tpmTask.Result.FirstOrDefault();
            if (tpmRow is not null)
            {
                tpmPresent   = tpmRow.TryGetValue("IsPresent", out var p)            && p is true;
                tpmEnabled   = tpmRow.TryGetValue("IsEnabled_InitialValue", out var e) && e is true;
                tpmActivated = tpmRow.TryGetValue("IsActivated_InitialValue", out var a) && a is true;
                tpmVersion   = tpmRow.TryGetValue("SpecVersion", out var v) ? v?.ToString() : null;
            }

            // VBS: EnableVirtualizationBasedSecurity registry
            var vbsEnabled = _registry.GetDword(RegistryHive.LocalMachine, DevGuardKey, "EnableVirtualizationBasedSecurity");
            // HVCI: Enabled registry under the scenario key
            var hvciEnabled = _registry.GetDword(RegistryHive.LocalMachine, HvciKey, "Enabled");

            // UEFI: check firmware type via registry (Boot\Status or BCD)
            // Simpler: check if SecureBoot cmdlet didn't throw (only on UEFI systems)
            var uefiMode = secureBootEnabled.HasValue;

            var result = new HardwareSecurity
            {
                SecureBootEnabled = secureBootEnabled,
                UefiMode          = uefiMode,
                TpmPresent        = tpmPresent,
                TpmEnabled        = tpmEnabled,
                TpmActivated      = tpmActivated,
                TpmVersion        = tpmVersion,
                VbsStatus         = (int)(vbsEnabled ?? 0),
                HvciStatus        = (int)(hvciEnabled ?? 0)
            };

            return CollectorResult<HardwareSecurity>.Ok(result, sw.Elapsed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "HardwareSecurity collection failed");
            return CollectorResult<HardwareSecurity>.Fail("HardwareSecurity collection failed", ex.ToString());
        }
    }
}
