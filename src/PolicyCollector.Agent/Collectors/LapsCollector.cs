using System.Diagnostics;
using PolicyCollector.Agent.Infrastructure;

namespace PolicyCollector.Agent.Collectors;

public sealed class LapsCollector : ICollector<LapsResult>
{
    private readonly RegistryReader _registry;
    private readonly ILogger<LapsCollector> _logger;

    // Windows LAPS (built-in since Windows 11 22H2 / Server 2025)
    private const string WinLapsKey    = @"SOFTWARE\Microsoft\Policies\LAPS";
    // Legacy LAPS (separate MSI install)
    private const string LegacyLapsKey = @"SOFTWARE\Policies\Microsoft Services\AdmPwd";

    public LapsCollector(RegistryReader registry, ILogger<LapsCollector> logger)
    {
        _registry = registry;
        _logger = logger;
    }

    public string ModuleName => "LAPS";

    public Task<CollectorResult<LapsResult>> CollectAsync(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            LapsResult result;

            if (_registry.KeyExists(RegistryHive.LocalMachine, WinLapsKey))
            {
                result = ReadWindowsLaps();
            }
            else if (_registry.KeyExists(RegistryHive.LocalMachine, LegacyLapsKey))
            {
                result = ReadLegacyLaps();
            }
            else
            {
                result = new LapsResult { PolicyConfigured = false };
            }

            return Task.FromResult(CollectorResult<LapsResult>.Ok(result, sw.Elapsed));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LAPS collection failed");
            return Task.FromResult(CollectorResult<LapsResult>.Fail("LAPS collection failed", ex.ToString()));
        }
    }

    private LapsResult ReadWindowsLaps()
    {
        // BackupDirectory: 0=disabled,1=AD,2=AAD
        var backupDir = _registry.GetDword(RegistryHive.LocalMachine, WinLapsKey, "BackupDirectory");
        var expiryDays = _registry.GetDword(RegistryHive.LocalMachine, WinLapsKey, "PasswordExpirationProtectionEnabled");
        var adminName  = _registry.GetString(RegistryHive.LocalMachine, WinLapsKey, "AdministratorAccountName");
        var passAgeDays = _registry.GetDword(RegistryHive.LocalMachine, WinLapsKey, "PasswordAgeDays");

        return new LapsResult
        {
            Type             = "WindowsLAPS",
            PolicyConfigured = true,
            BackupDirectory  = backupDir switch { 1 => "AD", 2 => "AAD", _ => null },
            PasswordExpiryDays = (int?)passAgeDays,
            AdminAccountName = adminName
        };
    }

    private LapsResult ReadLegacyLaps()
    {
        var enabled    = _registry.GetDword(RegistryHive.LocalMachine, LegacyLapsKey, "AdmPwdEnabled") == 1;
        var passAgeDays = _registry.GetDword(RegistryHive.LocalMachine, LegacyLapsKey, "PasswordAgeDays");
        var adminName  = _registry.GetString(RegistryHive.LocalMachine, LegacyLapsKey, "AdminAccountName");

        return new LapsResult
        {
            Type             = "LegacyLAPS",
            PolicyConfigured = enabled,
            BackupDirectory  = "AD",
            PasswordExpiryDays = (int?)passAgeDays,
            AdminAccountName = adminName
        };
    }
}
