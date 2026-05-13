# P2 — Agent: Collectors

**Phase:** 1 | **Phụ thuộc:** P1 hoàn thành | **Output:** 9 collectors đầy đủ + unit tests

## Mục tiêu

Implement toàn bộ collectors. Mỗi collector độc lập, không ảnh hưởng lẫn nhau khi lỗi. Sau P2, `CollectionJob` có thể gọi tất cả và nhận payload đầy đủ.

## Files cần tạo

```
src/PolicyCollector.Agent/
  Collectors/
    HostInfoCollector.cs
    GpoCollector.cs
    SecurityPolicyCollector.cs
    FirewallCollector.cs
    DefenderCollector.cs
    BitLockerCollector.cs
    AppInventoryCollector.cs
    AppxCollector.cs
    ServiceCollector.cs
    ScheduledTaskCollector.cs
    StartupCollector.cs
  Jobs/
    CollectionJob.cs
    PayloadBuilder.cs

tests/PolicyCollector.Agent.Tests/
  Collectors/
    GpoCollectorTests.cs
    SecurityPolicyCollectorTests.cs
    AppInventoryCollectorTests.cs
    ServiceCollectorTests.cs
```

---

## Chi tiết từng Collector

### [FILE] `Collectors/HostInfoCollector.cs`

```csharp
namespace PolicyCollector.Agent.Collectors;

public sealed class HostInfoCollector : ICollector<HostInfo>
{
    public string ModuleName => "HostInfo";

    public async Task<CollectorResult<HostInfo>> CollectAsync(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var info = new HostInfo
            {
                Hostname    = Environment.MachineName,
                Fqdn        = GetFqdn(),          // Dns.GetHostEntry + Domain
                Domain      = GetDomain(),         // Registry NetSetup2 hoặc Environment
                DomainJoined = IsDomainJoined(),   // NetGetJoinInformation P/Invoke
                Workgroup   = GetWorkgroup(),
                OsName      = GetOsName(),         // Registry ProductName
                OsVersion   = Environment.OSVersion.Version.ToString(),
                OsBuild     = GetBuildNumber(),    // Registry CurrentBuildNumber + UBR
                OsEdition   = GetEdition(),        // Registry EditionID
                Architecture = Environment.Is64BitOperatingSystem ? "x64" : "x86",
                Locale      = CultureInfo.CurrentCulture.Name,
                Timezone    = TimeZoneInfo.Local.Id,
                LastBoot    = GetLastBootTime(),   // WMI Win32_OperatingSystem.LastBootUpTime
                IpAddresses = GetLocalIps()        // NetworkInterface.GetAllNetworkInterfaces()
            };
            return CollectorResult<HostInfo>.Ok(info, sw.Elapsed);
        }
        catch (Exception ex)
        {
            return CollectorResult<HostInfo>.Fail("HostInfo collection failed", ex.ToString());
        }
    }

    // Registry keys:
    //   HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProductName
    //   HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\CurrentBuildNumber
    //   HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\UBR (Update Build Revision)
    //   HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\EditionID
    //   HKLM\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Domain
    //   HKLM\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\NV Domain
    private string? GetDomain() { ··· }
    private string GetOsName() { ··· }
    private string GetBuildNumber() { ··· }
    private string? GetEdition() { ··· }
    private bool IsDomainJoined() { ··· }   // NetGetJoinInformation returns NetSetupDomainName
    private DateTimeOffset? GetLastBootTime() { ··· }
    private List<string> GetLocalIps() { ··· }
    private string GetFqdn() { ··· }
    private string? GetWorkgroup() { ··· }
}
```

---

### [FILE] `Collectors/GpoCollector.cs`

```csharp
namespace PolicyCollector.Agent.Collectors;

public sealed class GpoCollector : ICollector<GpoResult>
{
    private readonly ProcessRunner _process;
    private readonly RegistryReader _registry;
    private readonly ILogger<GpoCollector> _logger;

    public string ModuleName => "GPO";

    public async Task<CollectorResult<GpoResult>> CollectAsync(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            // Tạo temp file cho gpresult output
            var tempXml = Path.Combine(Path.GetTempPath(), $"gpresult_{Guid.NewGuid():N}.xml");
            try
            {
                var result = await _process.RunAsync(
                    "gpresult.exe",
                    $"/X \"{tempXml}\" /SCOPE COMPUTER /FORCE",
                    timeout: TimeSpan.FromSeconds(20),
                    ct);

                // Exit code 0 = success, 1 = no data (workgroup), 2 = access denied
                if (result.ExitCode == 2)
                    return CollectorResult<GpoResult>.Fail("gpresult: access denied");

                var gpoResult = result.ExitCode == 0
                    ? ParseGpresultXml(tempXml)
                    : new GpoResult { RefreshStatus = "NoData" };

                // Enrich với registry GPO history
                EnrichWithRegistryHistory(gpoResult);

                return CollectorResult<GpoResult>.Ok(gpoResult, sw.Elapsed);
            }
            finally
            {
                if (File.Exists(tempXml)) File.Delete(tempXml);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GPO collection failed");
            return CollectorResult<GpoResult>.Fail("GPO collection failed", ex.ToString());
        }
    }

    // Parse gpresult XML — XDocument navigation:
    // /RSoPData/ComputerResults/GPO[]/Name, GUID, Link, AppliedOrder, FilterAllowed
    // /RSoPData/ComputerResults/ExtensionData/Extension/q1:RegistrySettings  (CSE results)
    // Namespace: http://www.microsoft.com/GroupPolicy/Rsop
    private GpoResult ParseGpresultXml(string xmlPath) { ··· }

    // Registry: HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Group Policy\History\{GUID}\
    //   DisplayName, GPOLink, Version, FileSysPath, DSPath
    private void EnrichWithRegistryHistory(GpoResult result) { ··· }
}
```

---

### [FILE] `Collectors/SecurityPolicyCollector.cs`

```csharp
namespace PolicyCollector.Agent.Collectors;

public sealed class SecurityPolicyCollector : ICollector<SecPolicyResult>
{
    private readonly ProcessRunner _process;
    private readonly RegistryReader _registry;

    public string ModuleName => "SecurityPolicy";

    public async Task<CollectorResult<SecPolicyResult>> CollectAsync(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            // Chạy secedit và auditpol song song
            var tempInf = Path.Combine(Path.GetTempPath(), $"secedit_{Guid.NewGuid():N}.inf");
            try
            {
                var seceditTask = _process.RunAsync(
                    "secedit.exe",
                    $"/export /cfg \"{tempInf}\" /areas SECURITYPOLICY USER_RIGHTS /quiet",
                    TimeSpan.FromSeconds(15), ct);

                var auditpolTask = _process.RunAsync(
                    "auditpol.exe",
                    "/get /category:* /r",
                    TimeSpan.FromSeconds(10), ct);

                await Task.WhenAll(seceditTask, auditpolTask);

                var passwordPolicy = ParsePasswordPolicy(tempInf);
                var userRights = ParseUserRights(tempInf);
                var auditPolicy = ParseAuditPolicy(auditpolTask.Result.Stdout);

                var result = new SecPolicyResult
                {
                    PasswordPolicy = passwordPolicy,
                    AuditPolicy    = auditPolicy,
                    UserRights     = userRights,
                    Uac            = ReadUacFromRegistry(),
                    Tls            = ReadTlsFromRegistry(),
                    Rdp            = ReadRdpFromRegistry()
                };

                return CollectorResult<SecPolicyResult>.Ok(result, sw.Elapsed);
            }
            finally
            {
                if (File.Exists(tempInf)) File.Delete(tempInf);
            }
        }
        catch (Exception ex)
        {
            return CollectorResult<SecPolicyResult>.Fail("SecPolicy collection failed", ex.ToString());
        }
    }

    // secedit INF format — [System Access] section:
    //   MinimumPasswordLength = 8
    //   PasswordComplexity = 1
    //   MaximumPasswordAge = 90
    //   LockoutBadCount = 5
    private PasswordPolicy ParsePasswordPolicy(string infPath) { ··· }

    // secedit INF format — [Privilege Rights] section:
    //   SeDebugPrivilege = *S-1-5-32-544
    //   → Resolve SID to account name
    private Dictionary<string, List<string>> ParseUserRights(string infPath) { ··· }

    // auditpol CSV output:
    //   Machine Name,Category,Subcategory,Subcategory GUID,Inclusion Setting,...
    //   Map subcategory → category để build AuditPolicy object
    private AuditPolicy ParseAuditPolicy(string csvOutput) { ··· }

    // Registry paths for UAC:
    //   HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System
    //     EnableLUA, ConsentPromptBehaviorAdmin, PromptOnSecureDesktop
    private UacConfig ReadUacFromRegistry() { ··· }

    // Registry paths for TLS:
    //   HKLM\SYSTEM\CurrentControlSet\Control\SecurityProviders\SCHANNEL\Protocols\
    //     {SSL 2.0,SSL 3.0,TLS 1.0,TLS 1.1,TLS 1.2,TLS 1.3}\{Client,Server}\Enabled
    private TlsConfig ReadTlsFromRegistry() { ··· }

    // Registry paths for RDP:
    //   HKLM\SYSTEM\CurrentControlSet\Control\Terminal Server\fDenyTSConnections
    //   HKLM\SYSTEM\CurrentControlSet\Control\Terminal Server\WinStations\RDP-Tcp\
    //     SecurityLayer (NLA=2), PortNumber, LogonTimeout, fDisableCdm
    private RdpConfig ReadRdpFromRegistry() { ··· }
}
```

---

### [FILE] `Collectors/FirewallCollector.cs`

```csharp
namespace PolicyCollector.Agent.Collectors;

public sealed class FirewallCollector : ICollector<FirewallResult>
{
    private readonly PowerShellRunner _ps;
    private const int MaxRulesExport = 500;

    public string ModuleName => "Firewall";

    public async Task<CollectorResult<FirewallResult>> CollectAsync(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            // Get profiles
            var profileObjects = await _ps.RunScriptAsync(
                "Get-NetFirewallProfile | Select-Object Name,Enabled,DefaultInboundAction,DefaultOutboundAction | ConvertTo-Json -Compress",
                ct);

            // Get rules (enabled only first, then total count)
            var rulesObjects = await _ps.RunScriptAsync($@"
                $rules = Get-NetFirewallRule | Where-Object Enabled -eq True |
                    Select-Object -First {MaxRulesExport} Name,DisplayName,Direction,Action,
                                         Enabled,Profile,Description
                $rules | ConvertTo-Json -Compress", ct);

            var totalCount = await _ps.RunScriptAsync(
                "(Get-NetFirewallRule).Count", ct);

            var result = BuildFirewallResult(profileObjects, rulesObjects, totalCount);
            return CollectorResult<FirewallResult>.Ok(result, sw.Elapsed);
        }
        catch (Exception ex)
        {
            return CollectorResult<FirewallResult>.Fail("Firewall collection failed", ex.ToString());
        }
    }

    private FirewallResult BuildFirewallResult(
        IReadOnlyList<PSObject> profiles,
        IReadOnlyList<PSObject> rules,
        IReadOnlyList<PSObject> total) { ··· }
}
```

---

### [FILE] `Collectors/DefenderCollector.cs`

```csharp
namespace PolicyCollector.Agent.Collectors;

public sealed class DefenderCollector : ICollector<DefenderResult>
{
    private readonly PowerShellRunner _ps;
    public string ModuleName => "Defender";

    public async Task<CollectorResult<DefenderResult>> CollectAsync(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            // Get-MpPreference: cấu hình (exclusions, cloud protection, ...)
            // Get-MpComputerStatus: trạng thái hiện tại (signature version, enabled, ...)
            var prefTask = _ps.RunScriptAsync(
                "Get-MpPreference | Select-Object DisableRealtimeMonitoring," +
                "MAPSReporting,SubmitSamplesConsent,ExclusionPath,ExclusionExtension | ConvertTo-Json -Compress",
                ct);

            var statusTask = _ps.RunScriptAsync(
                "Get-MpComputerStatus | Select-Object AntivirusEnabled," +
                "RealTimeProtectionEnabled,AntispywareEnabled,NISEnabled," +
                "AntivirusSignatureVersion,AntispywareSignatureVersion," +
                "AntivirusSignatureLastUpdated | ConvertTo-Json -Compress",
                ct);

            await Task.WhenAll(prefTask, statusTask);

            // Parse PSObject → DefenderResult model
            var result = ParseDefenderResult(prefTask.Result, statusTask.Result);
            return CollectorResult<DefenderResult>.Ok(result, sw.Elapsed);
        }
        catch (Exception ex)
        {
            // Defender không cài → return partial result, không fail
            return CollectorResult<DefenderResult>.Fail("Defender collection failed", ex.ToString());
        }
    }

    private DefenderResult ParseDefenderResult(
        IReadOnlyList<PSObject> pref,
        IReadOnlyList<PSObject> status) { ··· }
}
```

---

### [FILE] `Collectors/BitLockerCollector.cs`

```csharp
namespace PolicyCollector.Agent.Collectors;

public sealed class BitLockerCollector : ICollector<List<BitLockerVolume>>
{
    private readonly PowerShellRunner _ps;
    public string ModuleName => "BitLocker";

    public async Task<CollectorResult<List<BitLockerVolume>>> CollectAsync(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var objects = await _ps.RunScriptAsync(
                "Get-BitLockerVolume | Select-Object MountPoint,VolumeStatus," +
                "EncryptionMethod,ProtectionStatus,EncryptionPercentage | ConvertTo-Json -Compress",
                ct);

            var volumes = ParseVolumes(objects);
            return CollectorResult<List<BitLockerVolume>>.Ok(volumes, sw.Elapsed);
        }
        catch (Exception ex)
        {
            // BitLocker module không có → không phải lỗi nghiêm trọng
            return CollectorResult<List<BitLockerVolume>>.Fail("BitLocker collection failed", ex.ToString());
        }
    }

    private List<BitLockerVolume> ParseVolumes(IReadOnlyList<PSObject> objects) { ··· }
}
```

---

### [FILE] `Collectors/AppInventoryCollector.cs`

```csharp
namespace PolicyCollector.Agent.Collectors;

public sealed class AppInventoryCollector : ICollector<List<AppEntry>>
{
    private readonly RegistryReader _registry;
    public string ModuleName => "AppInventory";

    // Không async thực sự — registry read là sync, wrap trong Task.Run
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

        // HKLM x64
        ReadFromKey(RegistryHive.LocalMachine,
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", "x64", results);

        // HKLM x86 (WOW6432Node)
        ReadFromKey(RegistryHive.LocalMachine,
            @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall", "x86", results);

        // Bỏ qua HKCU vì chạy dưới SYSTEM — không có user context
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

            results.Add(new AppEntry
            {
                DisplayName      = name,
                DisplayVersion   = _registry.GetString(hive, fullPath, "DisplayVersion"),
                Publisher        = _registry.GetString(hive, fullPath, "Publisher"),
                InstallDate      = _registry.GetString(hive, fullPath, "InstallDate"),
                InstallLocation  = _registry.GetString(hive, fullPath, "InstallLocation"),
                UninstallString  = _registry.GetString(hive, fullPath, "UninstallString"),
                Architecture     = arch,
                Source           = DetermineSource(
                    _registry.GetString(hive, fullPath, "UninstallString")),
                RegistryHive     = hive == RegistryHive.LocalMachine ? "HKLM" : "HKCU",
                RegistryKey      = fullPath
            });
        }
    }

    // MSI nếu UninstallString bắt đầu bằng "MsiExec.exe", còn lại là EXE
    private static string DetermineSource(string? uninstallString) { ··· }
}
```

---

### [FILE] `Collectors/AppxCollector.cs`

```csharp
namespace PolicyCollector.Agent.Collectors;

public sealed class AppxCollector : ICollector<List<AppxEntry>>
{
    private readonly PowerShellRunner _ps;
    public string ModuleName => "AppxPackages";

    public async Task<CollectorResult<List<AppxEntry>>> CollectAsync(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var objects = await _ps.RunScriptAsync(
                "Get-AppxPackage -AllUsers | Select-Object Name,Version,Publisher," +
                "Architecture,InstallLocation,PackageFullName | ConvertTo-Json -Compress",
                ct);

            var packages = ParsePackages(objects);
            return CollectorResult<List<AppxEntry>>.Ok(packages, sw.Elapsed);
        }
        catch (Exception ex)
        {
            return CollectorResult<List<AppxEntry>>.Fail("Appx collection failed", ex.ToString());
        }
    }

    private List<AppxEntry> ParsePackages(IReadOnlyList<PSObject> objects) { ··· }
}
```

---

### [FILE] `Collectors/ServiceCollector.cs`

```csharp
namespace PolicyCollector.Agent.Collectors;

public sealed class ServiceCollector : ICollector<List<ServiceEntry>>
{
    private readonly WmiQuery _wmi;
    public string ModuleName => "Services";

    public async Task<CollectorResult<List<ServiceEntry>>> CollectAsync(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            // WMI Win32_Service (không bao gồm driver — dùng Win32_Service chứ không Win32_SystemDriver)
            var rows = await _wmi.QueryAsync(
                "Win32_Service",
                properties: ["Name", "DisplayName", "State", "StartMode",
                             "StartName", "PathName", "Description", "ProcessId"],
                ct: ct);

            var services = rows
                .Select(r => new ServiceEntry
                {
                    Name         = r.GetString("Name"),
                    DisplayName  = r.GetString("DisplayName"),
                    Status       = r.GetString("State") ?? "Unknown",
                    StartupType  = MapStartMode(r.GetString("StartMode")),
                    Account      = r.GetString("StartName"),
                    BinaryPath   = r.GetString("PathName"),
                    Description  = r.GetString("Description"),
                    Pid          = r.GetInt("ProcessId")
                })
                .OrderBy(s => s.Name)
                .ToList();

            return CollectorResult<List<ServiceEntry>>.Ok(services, sw.Elapsed);
        }
        catch (Exception ex)
        {
            return CollectorResult<List<ServiceEntry>>.Fail("Services collection failed", ex.ToString());
        }
    }

    // Win32_Service StartMode: Auto, Manual, Disabled
    // Cần xử lý "Auto" → "Automatic" hoặc "AutomaticDelayedStart"
    // Delayed start detect: Registry HKLM\SYSTEM\...\Services\{name}\DelayedAutostart = 1
    private string MapStartMode(string? startMode) { ··· }
}
```

---

### [FILE] `Collectors/ScheduledTaskCollector.cs`

```csharp
namespace PolicyCollector.Agent.Collectors;

public sealed class ScheduledTaskCollector : ICollector<List<TaskEntry>>
{
    private readonly PowerShellRunner _ps;
    public string ModuleName => "ScheduledTasks";

    public async Task<CollectorResult<List<TaskEntry>>> CollectAsync(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            // Get-ScheduledTask + Get-ScheduledTaskInfo
            // Limit: bỏ qua \Microsoft\Windows\... (built-in tasks), chỉ lấy custom + top-level
            var objects = await _ps.RunScriptAsync(@"
                Get-ScheduledTask | Where-Object { $_.TaskPath -notlike '\Microsoft\*' -or $_.TaskPath -eq '\' } |
                ForEach-Object {
                    $info = $_ | Get-ScheduledTaskInfo -ErrorAction SilentlyContinue
                    [PSCustomObject]@{
                        TaskName     = $_.TaskName
                        TaskPath     = $_.TaskPath
                        State        = $_.State.ToString()
                        RunAsUser    = $_.Principal.UserId
                        LastRunTime  = $info.LastRunTime
                        LastRunResult = $info.LastTaskResult
                        NextRunTime  = $info.NextRunTime
                        Actions      = ($_.Actions | ConvertTo-Json -Compress)
                        Triggers     = ($_.Triggers | ConvertTo-Json -Compress)
                    }
                } | ConvertTo-Json -Compress", ct);

            var tasks = ParseTasks(objects);
            return CollectorResult<List<TaskEntry>>.Ok(tasks, sw.Elapsed);
        }
        catch (Exception ex)
        {
            return CollectorResult<List<TaskEntry>>.Fail("ScheduledTasks collection failed", ex.ToString());
        }
    }

    private List<TaskEntry> ParseTasks(IReadOnlyList<PSObject> objects) { ··· }
}
```

---

### [FILE] `Collectors/StartupCollector.cs`

```csharp
namespace PolicyCollector.Agent.Collectors;

public sealed class StartupCollector : ICollector<List<StartupEntry>>
{
    private readonly RegistryReader _registry;
    public string ModuleName => "StartupEntries";

    public async Task<CollectorResult<List<StartupEntry>>> CollectAsync(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var entries = await Task.Run(() =>
            {
                var result = new List<StartupEntry>();

                // HKLM Run (all users, x64)
                ReadRunKey(RegistryHive.LocalMachine,
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", result);

                // HKLM RunOnce
                ReadRunKey(RegistryHive.LocalMachine,
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce", result);

                // HKLM Run (x86 / WOW6432Node)
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
                Name     = name,
                Command  = value?.ToString(),
                Location = keyPath,
                Enabled  = true  // Run key = luôn enabled (RunOnce sẽ xóa sau run)
            });
        }
    }
}
```

---

### [FILE] `Jobs/CollectionJob.cs`

```csharp
namespace PolicyCollector.Agent.Jobs;

public sealed class CollectionJob
{
    private readonly IOptions<AgentOptions> _options;
    private readonly ICollector<HostInfo> _hostCollector;
    private readonly ICollector<GpoResult> _gpoCollector;
    private readonly ICollector<SecPolicyResult> _secPolicyCollector;
    private readonly ICollector<FirewallResult> _firewallCollector;
    private readonly ICollector<DefenderResult> _defenderCollector;
    private readonly ICollector<List<BitLockerVolume>> _bitLockerCollector;
    private readonly ICollector<List<AppEntry>> _appCollector;
    private readonly ICollector<List<AppxEntry>> _appxCollector;
    private readonly ICollector<List<ServiceEntry>> _serviceCollector;
    private readonly ICollector<List<TaskEntry>> _taskCollector;
    private readonly ICollector<List<StartupEntry>> _startupCollector;
    private readonly ILogger<CollectionJob> _logger;

    public async Task<CollectionPayload> RunAsync(CancellationToken ct)
    {
        _logger.LogInformation("Collection cycle starting");
        var startTime = DateTimeOffset.UtcNow;
        var modules = _options.Value.Modules;
        var timeout = _options.Value.CollectorTimeoutSeconds;

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(timeout + 10)); // buffer above per-collector timeout

        // Chạy song song tất cả collectors được bật
        var tasks = new List<Task>();
        HostInfo? hostInfo = null;
        GpoResult? gpo = null;
        // ... (declare all result variables)

        // Host luôn collect
        var hostTask = RunCollector(_hostCollector, timeout, cts.Token)
            .ContinueWith(t => hostInfo = t.Result?.Data, TaskContinuationOptions.OnlyOnRanToCompletion);
        tasks.Add(hostTask);

        if (modules.GPO)
        {
            var t = RunCollector(_gpoCollector, timeout, cts.Token)
                .ContinueWith(r => gpo = r.Result?.Data, TaskContinuationOptions.OnlyOnRanToCompletion);
            tasks.Add(t);
        }

        // ... tương tự cho tất cả modules

        await Task.WhenAll(tasks);

        var payload = PayloadBuilder.Build(startTime, hostInfo, gpo, /* ... */);
        _logger.LogInformation("Collection cycle completed in {Duration}ms",
            (DateTimeOffset.UtcNow - startTime).TotalMilliseconds);

        return payload;
    }

    // Wrap collector với per-collector timeout
    private async Task<CollectorResult<T>?> RunCollector<T>(
        ICollector<T> collector,
        int timeoutSeconds,
        CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        try
        {
            var result = await collector.CollectAsync(cts.Token);
            if (!result.Success)
                _logger.LogWarning("Collector {Module} failed: {Error}",
                    collector.ModuleName, result.ErrorMessage);
            return result;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Collector {Module} timed out after {Timeout}s",
                collector.ModuleName, timeoutSeconds);
            return null;
        }
    }
}
```

---

### [FILE] `Jobs/PayloadBuilder.cs`

```csharp
namespace PolicyCollector.Agent.Jobs;

// Static builder — không có state, pure function
public static class PayloadBuilder
{
    public static CollectionPayload Build(
        DateTimeOffset collectedAt,
        HostInfo? host,
        GpoResult? gpo,
        SecPolicyResult? secPolicy,
        FirewallResult? firewall,
        DefenderResult? defender,
        List<BitLockerVolume>? bitLocker,
        List<AppEntry>? applications,
        List<AppxEntry>? appxPackages,
        List<ServiceEntry>? services,
        List<TaskEntry>? scheduledTasks,
        List<StartupEntry>? startupEntries) =>
        new()
        {
            CollectionId   = Guid.NewGuid().ToString(),
            CollectedAt    = collectedAt,
            Host           = host,
            Gpo            = gpo,
            SecurityPolicy = secPolicy,
            Firewall       = firewall,
            Defender       = defender,
            BitLocker      = bitLocker,
            Applications   = applications,
            AppxPackages   = appxPackages,
            Services       = services,
            ScheduledTasks = scheduledTasks,
            StartupEntries = startupEntries
        };
}
```

---

## Unit Tests

### [TEST] `Collectors/GpoCollectorTests.cs`

```csharp
// Mock ProcessRunner để trả về fixture XML
// Test cases:
//   - ParseGpresultXml: file XML hợp lệ → correct GPO list
//   - ParseGpresultXml: file XML rỗng → empty list, no throw
//   - CollectAsync: exit code 2 → CollectorResult.Success=false
//   - CollectAsync: process timeout → CollectorResult.Success=false
//   - CollectAsync: gpresult thành công → GpoResult với applied GPOs
```

### [TEST] `Collectors/AppInventoryCollectorTests.cs`

```csharp
// Mock RegistryReader với fixture data
// Test cases:
//   - ReadFromKey: entry có DisplayName → included
//   - ReadFromKey: entry không có DisplayName → excluded
//   - ReadFromKey: registry key không tồn tại → empty list, no throw
//   - DetermineSource: "MsiExec.exe /X{...}" → "MSI"
//   - DetermineSource: "C:\path\uninstall.exe" → "EXE"
```

---

## Đăng ký DI (cập nhật Program.cs)

```csharp
// Sau khi P2 hoàn thành, thêm vào Program.cs:
builder.Services.AddSingleton<ICollector<HostInfo>, HostInfoCollector>();
builder.Services.AddSingleton<ICollector<GpoResult>, GpoCollector>();
builder.Services.AddSingleton<ICollector<SecPolicyResult>, SecurityPolicyCollector>();
builder.Services.AddSingleton<ICollector<FirewallResult>, FirewallCollector>();
builder.Services.AddSingleton<ICollector<DefenderResult>, DefenderCollector>();
builder.Services.AddSingleton<ICollector<List<BitLockerVolume>>, BitLockerCollector>();
builder.Services.AddSingleton<ICollector<List<AppEntry>>, AppInventoryCollector>();
builder.Services.AddSingleton<ICollector<List<AppxEntry>>, AppxCollector>();
builder.Services.AddSingleton<ICollector<List<ServiceEntry>>, ServiceCollector>();
builder.Services.AddSingleton<ICollector<List<TaskEntry>>, ScheduledTaskCollector>();
builder.Services.AddSingleton<ICollector<List<StartupEntry>>, StartupCollector>();
builder.Services.AddSingleton<ICollector<AdInfo>, AdCollector>();
builder.Services.AddSingleton<ICollector<RegistryAuditResult>, RegistryAuditCollector>();
```

---

### [FILE] `Collectors/AdCollector.cs`

Thu thập thông tin Active Directory cho máy đã join domain.

- Kiểm tra domain-joined qua `HKLM\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Domain`
- Chạy `nltest /dsgetdc:{domain}` để lấy DC name và AD site
- Lấy OU/Distinguished-Name từ `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Group Policy\State\Machine\Distinguished-Name`
- Trả về `AdInfo { KerberosAvailable=false }` nếu không join domain (không fail)

**Modules flag:** `ActiveDirectory` (default: `true`)

---

### [FILE] `Collectors/RegistryAuditCollector.cs`

Kiểm tra các registry key nguy hiểm ảnh hưởng đến bảo mật.

**Sections thu thập:**
| Section | Registry path |
|---|---|
| LSA | `HKLM\SYSTEM\...\Control\Lsa` |
| WDigest | `HKLM\SYSTEM\...\SecurityProviders\WDigest` |
| SMB | `HKLM\SYSTEM\...\Services\LanmanServer\Parameters` |
| PowerShell Policy | `HKLM\SOFTWARE\Policies\Microsoft\Windows\PowerShell` |
| Winlogon | `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon` |
| Credential Guard | `HKLM\SYSTEM\...\Control\DeviceGuard` |

**`DangerousFlags` được tự động compute** từ các giá trị đọc được. Mỗi flag có: `name`, `registry_path`, `value_name`, `actual_value`, `expected_value`, `severity` (critical/high/medium/low), `description`.

Các check critical:
- WDigest `UseLogonCredential=1` → plain-text creds in LSASS
- Winlogon `Shell` không phải `explorer.exe` → backdoor
- Winlogon `Userinit` bị sửa → persistence

Các check high:
- `LmCompatibilityLevel < 3` → NTLMv1 downgrade
- `DisableRestrictedAdmin=1` → pass-the-hash via RDP
- `SMB1=1` hoặc `mrxsmb10\Start != 4` → EternalBlue risk
- `AutoAdminLogon=1` → cleartext creds in registry
- `LocalAccountTokenFilterPolicy=1` → lateral movement via PtH

**Modules flag:** `RegistryAudit` (default: `true`)

## Acceptance Criteria

- [ ] Mỗi collector trả về `CollectorResult.Success=false` thay vì throw khi lỗi
- [ ] `CollectionJob` vẫn hoàn thành dù 1 collector fail
- [ ] `AppInventoryCollector` trả về list rỗng (không null) khi registry key không tồn tại
- [ ] `GpoCollector` xóa temp XML file ngay cả khi parse lỗi
- [ ] `PayloadBuilder.Build()` không throw với toàn bộ null parameters
- [ ] Unit tests pass: ≥ 80% coverage cho Collectors
