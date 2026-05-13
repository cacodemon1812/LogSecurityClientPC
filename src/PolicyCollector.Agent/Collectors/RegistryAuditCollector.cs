using System.Diagnostics;

namespace PolicyCollector.Agent.Collectors;

public sealed class RegistryAuditCollector : ICollector<RegistryAuditResult>
{
    private readonly RegistryReader _registry;
    private readonly ILogger<RegistryAuditCollector> _logger;

    private const string LsaKey       = @"SYSTEM\CurrentControlSet\Control\Lsa";
    private const string WDigestKey    = @"SYSTEM\CurrentControlSet\Control\SecurityProviders\WDigest";
    private const string SmbServerKey  = @"SYSTEM\CurrentControlSet\Services\LanmanServer\Parameters";
    private const string SmbWorkstnKey = @"SYSTEM\CurrentControlSet\Services\LanmanWorkstation\Parameters";
    private const string Smb1DriverKey = @"SYSTEM\CurrentControlSet\Services\mrxsmb10";
    private const string PsPolicyKey   = @"SOFTWARE\Policies\Microsoft\Windows\PowerShell";
    private const string PsSblKey      = @"SOFTWARE\Policies\Microsoft\Windows\PowerShell\ScriptBlockLogging";
    private const string PsTransKey    = @"SOFTWARE\Policies\Microsoft\Windows\PowerShell\Transcription";
    private const string WinlogonKey   = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon";
    private const string DevGuardKey   = @"SYSTEM\CurrentControlSet\Control\DeviceGuard";
    private const string UacPoliciesKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System";

    public RegistryAuditCollector(RegistryReader registry, ILogger<RegistryAuditCollector> logger)
    {
        _registry = registry;
        _logger = logger;
    }

    public string ModuleName => "RegistryAudit";

    public Task<CollectorResult<RegistryAuditResult>> CollectAsync(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var lsa        = ReadLsa();
            var wdigest    = ReadWDigest();
            var smb        = ReadSmb();
            var psPolicy   = ReadPowerShellPolicy();
            var winlogon   = ReadWinlogon();
            var credGuard  = ReadCredentialGuard();

            var flags = new List<DangerousFlag>();
            EvaluateDangerousFlags(flags, lsa, wdigest, smb, psPolicy, winlogon, credGuard);

            var result = new RegistryAuditResult
            {
                Lsa             = lsa,
                WDigest         = wdigest,
                Smb             = smb,
                PowerShellPolicy = psPolicy,
                Winlogon        = winlogon,
                CredentialGuard = credGuard,
                DangerousFlags  = flags
            };

            return Task.FromResult(CollectorResult<RegistryAuditResult>.Ok(result, sw.Elapsed));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RegistryAudit collection failed");
            return Task.FromResult(CollectorResult<RegistryAuditResult>.Fail(
                "RegistryAudit collection failed", ex.ToString()));
        }
    }

    private LsaSettings ReadLsa()
    {
        int? lmCompatLevel = _registry.GetDword(RegistryHive.LocalMachine, LsaKey, "LmCompatibilityLevel");
        int? noLmHash      = _registry.GetDword(RegistryHive.LocalMachine, LsaKey, "NoLMHash");
        int? restrictAnon  = _registry.GetDword(RegistryHive.LocalMachine, LsaKey, "RestrictAnonymous");
        int? restrictSam   = _registry.GetDword(RegistryHive.LocalMachine, LsaKey, "RestrictAnonymousSAM");
        int? disableRA     = _registry.GetDword(RegistryHive.LocalMachine, LsaKey, "DisableRestrictedAdmin");
        int? runAsPpl      = _registry.GetDword(RegistryHive.LocalMachine, LsaKey, "RunAsPPL");
        int? disableDomCreds = _registry.GetDword(RegistryHive.LocalMachine, LsaKey, "DisableDomainCreds");
        int? latfp         = _registry.GetDword(RegistryHive.LocalMachine, UacPoliciesKey, "LocalAccountTokenFilterPolicy");

        return new LsaSettings
        {
            LmCompatLevel              = lmCompatLevel,
            NoLmHash                   = noLmHash.HasValue ? noLmHash == 1 : null,
            RestrictAnonymous          = restrictAnon,
            RestrictAnonymousSam       = restrictSam.HasValue ? restrictSam == 1 : null,
            DisableRestrictedAdmin     = disableRA.HasValue ? disableRA == 1 : null,
            RunAsPpl                   = runAsPpl.HasValue ? runAsPpl == 1 : null,
            DisableDomainCreds         = disableDomCreds.HasValue ? disableDomCreds == 1 : null,
            LocalAccountTokenFilterPolicy = latfp.HasValue ? latfp == 1 : null
        };
    }

    private WDigestSettings ReadWDigest()
    {
        var val = _registry.GetDword(RegistryHive.LocalMachine, WDigestKey, "UseLogonCredential");
        return new WDigestSettings
        {
            UseLogonCredential = val.HasValue ? val == 1 : null
        };
    }

    private SmbSettings ReadSmb()
    {
        int? smb1         = _registry.GetDword(RegistryHive.LocalMachine, SmbServerKey, "SMB1");
        int? driverStart  = _registry.GetDword(RegistryHive.LocalMachine, Smb1DriverKey, "Start");
        int? srvSigning   = _registry.GetDword(RegistryHive.LocalMachine, SmbServerKey, "EnableSecuritySignature");
        int? cliSignReq   = _registry.GetDword(RegistryHive.LocalMachine, SmbWorkstnKey, "RequireSecuritySignature");

        // SMB1 key absent means the feature is not installed (safe); key=1 means explicitly enabled
        bool? smb1Enabled = smb1.HasValue ? smb1 == 1 : null;

        return new SmbSettings
        {
            Smb1Enabled           = smb1Enabled,
            Smb1DriverStart       = driverStart,
            ServerSigningEnabled  = srvSigning.HasValue ? srvSigning == 1 : null,
            ClientSigningRequired = cliSignReq.HasValue ? cliSignReq == 1 : null
        };
    }

    private PowerShellPolicy ReadPowerShellPolicy()
    {
        var execPolicy = _registry.GetString(RegistryHive.LocalMachine, PsPolicyKey, "ExecutionPolicy");
        int? sbl       = _registry.GetDword(RegistryHive.LocalMachine, PsSblKey, "EnableScriptBlockLogging");
        int? trans     = _registry.GetDword(RegistryHive.LocalMachine, PsTransKey, "EnableTranscripting");

        return new PowerShellPolicy
        {
            ExecutionPolicy    = execPolicy,
            ScriptBlockLogging = sbl.HasValue ? sbl == 1 : null,
            Transcription      = trans.HasValue ? trans == 1 : null
        };
    }

    private WinlogonSettings ReadWinlogon()
    {
        var userinit      = _registry.GetString(RegistryHive.LocalMachine, WinlogonKey, "Userinit");
        var shell         = _registry.GetString(RegistryHive.LocalMachine, WinlogonKey, "Shell");
        int? autoAdminLogon = _registry.GetDword(RegistryHive.LocalMachine, WinlogonKey, "AutoAdminLogon");

        return new WinlogonSettings
        {
            Userinit      = userinit,
            Shell         = shell,
            AutoAdminLogon = autoAdminLogon.HasValue ? autoAdminLogon == 1 : null
        };
    }

    private CredentialGuardSettings ReadCredentialGuard()
    {
        int? vbs          = _registry.GetDword(RegistryHive.LocalMachine, DevGuardKey, "EnableVirtualizationBasedSecurity");
        int? lsaCfgFlags  = _registry.GetDword(RegistryHive.LocalMachine, LsaKey, "LsaCfgFlags");

        return new CredentialGuardSettings
        {
            VbsEnabled  = vbs.HasValue ? vbs == 1 : null,
            LsaCfgFlags = lsaCfgFlags
        };
    }

    private static void EvaluateDangerousFlags(
        List<DangerousFlag> flags,
        LsaSettings lsa,
        WDigestSettings wdigest,
        SmbSettings smb,
        PowerShellPolicy psPolicy,
        WinlogonSettings winlogon,
        CredentialGuardSettings credGuard)
    {
        // WDigest: plain-text credentials cached in LSASS memory
        if (wdigest.UseLogonCredential == true)
            flags.Add(new DangerousFlag
            {
                Name         = "WDigest_PlaintextCredentials",
                RegistryPath = @"HKLM\" + WDigestKey,
                ValueName    = "UseLogonCredential",
                ActualValue  = "1",
                ExpectedValue = "0",
                Severity     = "critical",
                Description  = "WDigest stores plain-text credentials in LSASS — trivially dumped by Mimikatz"
            });

        // LM Compatibility Level: NTLMv1 downgrade
        if (lsa.LmCompatLevel.HasValue && lsa.LmCompatLevel.Value < 3)
            flags.Add(new DangerousFlag
            {
                Name         = "NTLM_LmCompatLevel_Weak",
                RegistryPath = @"HKLM\" + LsaKey,
                ValueName    = "LmCompatibilityLevel",
                ActualValue  = lsa.LmCompatLevel.Value.ToString(),
                ExpectedValue = "5",
                Severity     = lsa.LmCompatLevel.Value < 2 ? "critical" : "high",
                Description  = "Weak NTLM authentication level — NTLMv1/LM responses accepted, relay/crack attacks possible"
            });

        // LM hash stored
        if (lsa.NoLmHash == false)
            flags.Add(new DangerousFlag
            {
                Name         = "LmHash_Stored",
                RegistryPath = @"HKLM\" + LsaKey,
                ValueName    = "NoLMHash",
                ActualValue  = "0",
                ExpectedValue = "1",
                Severity     = "high",
                Description  = "LM password hashes stored in SAM — vulnerable to offline rainbow-table attacks"
            });

        // DisableRestrictedAdmin = 1 → RDP restricted admin OFF → pass-the-hash via RDP
        if (lsa.DisableRestrictedAdmin == true)
            flags.Add(new DangerousFlag
            {
                Name         = "RDP_RestrictedAdmin_Disabled",
                RegistryPath = @"HKLM\" + LsaKey,
                ValueName    = "DisableRestrictedAdmin",
                ActualValue  = "1",
                ExpectedValue = "0",
                Severity     = "high",
                Description  = "RDP Restricted Admin disabled — attacker with NTLM hash can open interactive RDP session"
            });

        // LSASS not running as PPL
        if (lsa.RunAsPpl == false || lsa.RunAsPpl is null)
            flags.Add(new DangerousFlag
            {
                Name         = "LSASS_PPL_Disabled",
                RegistryPath = @"HKLM\" + LsaKey,
                ValueName    = "RunAsPPL",
                ActualValue  = lsa.RunAsPpl.HasValue ? "0" : "(not set)",
                ExpectedValue = "1",
                Severity     = "medium",
                Description  = "LSASS not running as Protected Process Light — credential dumping possible without driver"
            });

        // SMBv1 server parameter explicitly enabled
        if (smb.Smb1Enabled == true)
            flags.Add(new DangerousFlag
            {
                Name         = "SMBv1_Enabled",
                RegistryPath = @"HKLM\" + SmbServerKey,
                ValueName    = "SMB1",
                ActualValue  = "1",
                ExpectedValue = "0",
                Severity     = "high",
                Description  = "SMBv1 protocol enabled — EternalBlue / WannaCry lateral movement risk"
            });

        // SMBv1 driver still startable (Start != 4)
        if (smb.Smb1DriverStart.HasValue && smb.Smb1DriverStart.Value != 4)
            flags.Add(new DangerousFlag
            {
                Name         = "SMBv1_Driver_Active",
                RegistryPath = @"HKLM\" + Smb1DriverKey,
                ValueName    = "Start",
                ActualValue  = smb.Smb1DriverStart.Value.ToString(),
                ExpectedValue = "4",
                Severity     = "high",
                Description  = "SMBv1 kernel driver (mrxsmb10) not disabled — network-level exploitation possible"
            });

        // Winlogon Shell hijacked
        if (!string.IsNullOrEmpty(winlogon.Shell) &&
            !winlogon.Shell.Equals("explorer.exe", StringComparison.OrdinalIgnoreCase))
            flags.Add(new DangerousFlag
            {
                Name         = "Winlogon_Shell_Modified",
                RegistryPath = @"HKLM\" + WinlogonKey,
                ValueName    = "Shell",
                ActualValue  = winlogon.Shell,
                ExpectedValue = "explorer.exe",
                Severity     = "critical",
                Description  = "Non-standard Windows shell — likely persistence backdoor"
            });

        // Winlogon Userinit modified
        if (!string.IsNullOrEmpty(winlogon.Userinit) && !IsDefaultUserinit(winlogon.Userinit))
            flags.Add(new DangerousFlag
            {
                Name         = "Winlogon_Userinit_Modified",
                RegistryPath = @"HKLM\" + WinlogonKey,
                ValueName    = "Userinit",
                ActualValue  = winlogon.Userinit,
                ExpectedValue = @"C:\Windows\system32\userinit.exe,",
                Severity     = "critical",
                Description  = "Modified Userinit value — additional executable runs on every user logon"
            });

        // AutoAdminLogon: credentials stored in cleartext registry
        if (winlogon.AutoAdminLogon == true)
            flags.Add(new DangerousFlag
            {
                Name         = "AutoAdminLogon_Enabled",
                RegistryPath = @"HKLM\" + WinlogonKey,
                ValueName    = "AutoAdminLogon",
                ActualValue  = "1",
                ExpectedValue = "0",
                Severity     = "high",
                Description  = "Automatic logon enabled — credentials stored in cleartext at DefaultPassword registry value"
            });

        // Anonymous access to SAM/shares
        if (lsa.RestrictAnonymous == 0)
            flags.Add(new DangerousFlag
            {
                Name         = "Anonymous_Access_Unrestricted",
                RegistryPath = @"HKLM\" + LsaKey,
                ValueName    = "RestrictAnonymous",
                ActualValue  = "0",
                ExpectedValue = "≥1",
                Severity     = "medium",
                Description  = "Anonymous users can enumerate SAM accounts and network shares"
            });

        // LocalAccountTokenFilterPolicy: remote UAC bypass for local admins
        if (lsa.LocalAccountTokenFilterPolicy == true)
            flags.Add(new DangerousFlag
            {
                Name         = "LocalAccountUAC_Bypass",
                RegistryPath = @"HKLM\" + UacPoliciesKey,
                ValueName    = "LocalAccountTokenFilterPolicy",
                ActualValue  = "1",
                ExpectedValue = "0",
                Severity     = "high",
                Description  = "Remote UAC filtering disabled for local admin accounts — enables pass-the-hash lateral movement"
            });

        // PowerShell: dangerous execution policy
        if (psPolicy.ExecutionPolicy is not null &&
            (psPolicy.ExecutionPolicy.Equals("Unrestricted", StringComparison.OrdinalIgnoreCase) ||
             psPolicy.ExecutionPolicy.Equals("Bypass", StringComparison.OrdinalIgnoreCase)))
            flags.Add(new DangerousFlag
            {
                Name         = "PowerShell_ExecutionPolicy_Open",
                RegistryPath = @"HKLM\" + PsPolicyKey,
                ValueName    = "ExecutionPolicy",
                ActualValue  = psPolicy.ExecutionPolicy,
                ExpectedValue = "RemoteSigned / AllSigned",
                Severity     = "medium",
                Description  = "PowerShell execution policy allows all scripts — malicious scripts can run unblocked"
            });
    }

    private static bool IsDefaultUserinit(string value)
    {
        var normalized = value.Trim().TrimEnd(',');
        return normalized.Equals(
            @"C:\Windows\system32\userinit.exe",
            StringComparison.OrdinalIgnoreCase);
    }
}
