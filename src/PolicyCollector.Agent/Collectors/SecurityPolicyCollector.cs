using System.Diagnostics;
using System.Security.Principal;
using System.Text.RegularExpressions;
using PolicyCollector.Agent.Infrastructure;

namespace PolicyCollector.Agent.Collectors;

public sealed class SecurityPolicyCollector : ICollector<SecPolicyResult>
{
    private readonly ProcessRunner _process;
    private readonly RegistryReader _registry;
    private readonly ILogger<SecurityPolicyCollector> _logger;

    public SecurityPolicyCollector(ProcessRunner process, RegistryReader registry, ILogger<SecurityPolicyCollector> logger)
    {
        _process = process;
        _registry = registry;
        _logger = logger;
    }

    public string ModuleName => "SecurityPolicy";

    public async Task<CollectorResult<SecPolicyResult>> CollectAsync(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
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

                var seceditOk   = seceditTask.Result.ExitCode == 0 && File.Exists(tempInf);
                var auditpolOk  = auditpolTask.Result.ExitCode == 0 &&
                                  !string.IsNullOrWhiteSpace(auditpolTask.Result.Stdout);

                if (!seceditOk)
                    _logger.LogWarning(
                        "secedit export failed (exitCode={ExitCode}, fileCreated={FileCreated}) — " +
                        "PasswordPolicy and UserRights will be empty. " +
                        "Run the service as LocalSystem or a member of local Administrators.",
                        seceditTask.Result.ExitCode, File.Exists(tempInf));

                if (!auditpolOk)
                    _logger.LogWarning(
                        "auditpol failed (exitCode={ExitCode}) — AuditPolicy will be empty. " +
                        "Run the service as LocalSystem or a member of local Administrators " +
                        "(requires SeSecurityPrivilege).",
                        auditpolTask.Result.ExitCode);

                var result = new SecPolicyResult
                {
                    PasswordPolicy = seceditOk  ? ParsePasswordPolicy(tempInf)          : new PasswordPolicy(),
                    UserRights     = seceditOk  ? ParseUserRights(tempInf)               : [],
                    AuditPolicy    = auditpolOk ? ParseAuditPolicy(auditpolTask.Result.Stdout) : new AuditPolicy(),
                    Uac            = ReadUacFromRegistry(),
                    Tls            = ReadTlsFromRegistry(),
                    Rdp            = ReadRdpFromRegistry()
                };

                return CollectorResult<SecPolicyResult>.Ok(result, sw.Elapsed);
            }
            finally
            {
                try { if (File.Exists(tempInf)) File.Delete(tempInf); }
                catch (IOException) { /* best-effort temp file cleanup */ }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SecPolicy collection failed");
            return CollectorResult<SecPolicyResult>.Fail("SecPolicy collection failed", ex.ToString());
        }
    }

    private PasswordPolicy ParsePasswordPolicy(string infPath)
    {
        try
        {
            var content = File.ReadAllText(infPath);
            var section = ExtractIniSection(content, "[System Access]");

            return new PasswordPolicy
            {
                MinLength          = ExtractIniValue<int>(section, "MinimumPasswordLength"),
                ComplexityEnabled  = ExtractIniValue<int>(section, "PasswordComplexity") == 1,
                MaxAgeDays         = ExtractIniValue<int>(section, "MaximumPasswordAge"),
                MinAgeDays         = ExtractIniValue<int>(section, "MinimumPasswordAge"),
                HistoryCount       = ExtractIniValue<int>(section, "PasswordHistorySize"),
                LockoutThreshold   = ExtractIniValue<int>(section, "LockoutBadCount"),
                LockoutDurationMin = ExtractIniValue<int>(section, "LockoutDuration"),
                LockoutWindowMin   = ExtractIniValue<int>(section, "ResetLockoutCount")
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse password policy");
            return new PasswordPolicy();
        }
    }

    private Dictionary<string, List<string>> ParseUserRights(string infPath)
    {
        var rights = new Dictionary<string, List<string>>();
        try
        {
            var content = File.ReadAllText(infPath);
            var section = ExtractIniSection(content, "[Privilege Rights]");

            foreach (var line in section.Split('\n'))
            {
                var match = Regex.Match(line, @"^(.+?)\s*=\s*(.+)$");
                if (!match.Success) continue;

                var rightName = match.Groups[1].Value.Trim();
                var sids = match.Groups[2].Value.Trim()
                    .Split(',')
                    .Select(s => s.Trim().TrimStart('*'))
                    .Where(s => !string.IsNullOrEmpty(s))
                    .Select(ResolveSid)
                    .ToList();

                if (sids.Count > 0)
                    rights[rightName] = sids;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse user rights");
        }

        return rights;
    }

    private AuditPolicy ParseAuditPolicy(string csvOutput)
    {
        string? logonLogoff = null;
        string? accountLogon = null;
        string? accountManagement = null;
        string? detailedTracking = null;
        string? objectAccess = null;
        string? policyChange = null;

        try
        {
            foreach (var line in csvOutput.Split('\n').Skip(1))
            {
                var parts = line.Split(',');
                if (parts.Length < 5) continue;

                var category = parts[1].Trim().Trim('"');
                var setting  = parts[4].Trim().Trim('"');

                if (string.IsNullOrEmpty(setting)) continue;

                if (category.Contains("Logon/Logoff", StringComparison.OrdinalIgnoreCase) && logonLogoff is null)
                    logonLogoff = setting;
                else if (category.Contains("Account Logon", StringComparison.OrdinalIgnoreCase) && accountLogon is null)
                    accountLogon = setting;
                else if (category.Contains("Account Management", StringComparison.OrdinalIgnoreCase) && accountManagement is null)
                    accountManagement = setting;
                else if (category.Contains("Detailed Tracking", StringComparison.OrdinalIgnoreCase) && detailedTracking is null)
                    detailedTracking = setting;
                else if (category.Contains("Object Access", StringComparison.OrdinalIgnoreCase) && objectAccess is null)
                    objectAccess = setting;
                else if (category.Contains("Policy Change", StringComparison.OrdinalIgnoreCase) && policyChange is null)
                    policyChange = setting;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse audit policy");
        }

        return new AuditPolicy
        {
            LogonLogoff        = logonLogoff,
            AccountLogon       = accountLogon,
            AccountManagement  = accountManagement,
            DetailedTracking   = detailedTracking,
            ObjectAccess       = objectAccess,
            PolicyChange       = policyChange
        };
    }

    private UacConfig ReadUacFromRegistry()
    {
        try
        {
            var key = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System";
            return new UacConfig
            {
                Enabled            = _registry.GetDword(RegistryHive.LocalMachine, key, "EnableLUA") == 1,
                ConsentPromptLevel = _registry.GetDword(RegistryHive.LocalMachine, key, "ConsentPromptBehaviorAdmin") ?? 0,
                SecureDesktop      = _registry.GetDword(RegistryHive.LocalMachine, key, "PromptOnSecureDesktop") == 1
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read UAC config");
            return new UacConfig();
        }
    }

    private TlsConfig ReadTlsFromRegistry()
    {
        try
        {
            const string serverSide = "Server";
            var basePath = @"SYSTEM\CurrentControlSet\Control\SecurityProviders\SCHANNEL\Protocols";

            bool IsEnabled(string ver) =>
                _registry.GetDword(RegistryHive.LocalMachine,
                    $@"{basePath}\{ver}\{serverSide}", "Enabled") == 1;

            return new TlsConfig
            {
                Protocols = new TlsProtocols
                {
                    Ssl20 = IsEnabled("SSL 2.0"),
                    Ssl30 = IsEnabled("SSL 3.0"),
                    Tls10 = IsEnabled("TLS 1.0"),
                    Tls11 = IsEnabled("TLS 1.1"),
                    Tls12 = IsEnabled("TLS 1.2"),
                    Tls13 = IsEnabled("TLS 1.3")
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read TLS config");
            return new TlsConfig();
        }
    }

    private RdpConfig ReadRdpFromRegistry()
    {
        try
        {
            var tsKey  = @"SYSTEM\CurrentControlSet\Control\Terminal Server";
            var rdpKey = $@"{tsKey}\WinStations\RDP-Tcp";

            var denyConnections = _registry.GetDword(RegistryHive.LocalMachine, tsKey, "fDenyTSConnections") == 1;
            var securityLayer   = _registry.GetDword(RegistryHive.LocalMachine, rdpKey, "SecurityLayer");
            var port            = _registry.GetDword(RegistryHive.LocalMachine, rdpKey, "PortNumber");

            return new RdpConfig
            {
                Enabled              = !denyConnections,
                NlaRequired          = securityLayer == 2,
                Port                 = (int)(port > 0 ? port : 3389)!,
                SessionTimeoutMin    = (_registry.GetDword(RegistryHive.LocalMachine, rdpKey, "MaxIdleTime") ?? 0) / 60000,
                DisconnectTimeoutMin = (_registry.GetDword(RegistryHive.LocalMachine, rdpKey, "MaxDisconnectionTime") ?? 0) / 60000
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read RDP config");
            return new RdpConfig();
        }
    }

    private static string ExtractIniSection(string content, string sectionName)
    {
        var startIdx = content.IndexOf(sectionName, StringComparison.OrdinalIgnoreCase);
        if (startIdx == -1) return string.Empty;

        var endIdx = content.IndexOf('[', startIdx + sectionName.Length);
        return endIdx == -1
            ? content[(startIdx + sectionName.Length)..]
            : content[(startIdx + sectionName.Length)..endIdx];
    }

    private static T ExtractIniValue<T>(string section, string key) where T : struct
    {
        var match = Regex.Match(section, $@"{key}\s*=\s*(-?\d+)");
        if (match.Success && typeof(T) == typeof(int))
            return (T)(object)int.Parse(match.Groups[1].Value);

        return default;
    }

    private static string ResolveSid(string sid)
    {
        try
        {
            if (sid.StartsWith("S-1-", StringComparison.Ordinal))
                return new SecurityIdentifier(sid).Translate(typeof(NTAccount)).Value;
            return sid;
        }
        catch
        {
            return sid;
        }
    }
}
