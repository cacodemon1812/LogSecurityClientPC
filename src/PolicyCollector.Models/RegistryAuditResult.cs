using System.Text.Json.Serialization;

namespace PolicyCollector.Agent.Models;

public sealed class RegistryAuditResult
{
    [JsonPropertyName("lsa")] public LsaSettings? Lsa { get; init; }
    [JsonPropertyName("wdigest")] public WDigestSettings? WDigest { get; init; }
    [JsonPropertyName("smb")] public SmbSettings? Smb { get; init; }
    [JsonPropertyName("powershell_policy")] public PowerShellPolicy? PowerShellPolicy { get; init; }
    [JsonPropertyName("winlogon")] public WinlogonSettings? Winlogon { get; init; }
    [JsonPropertyName("credential_guard")] public CredentialGuardSettings? CredentialGuard { get; init; }
    [JsonPropertyName("dangerous_flags")] public List<DangerousFlag> DangerousFlags { get; init; } = [];
}

public sealed class LsaSettings
{
    [JsonPropertyName("lm_compat_level")] public int? LmCompatLevel { get; init; }
    [JsonPropertyName("no_lm_hash")] public bool? NoLmHash { get; init; }
    [JsonPropertyName("restrict_anonymous")] public int? RestrictAnonymous { get; init; }
    [JsonPropertyName("restrict_anonymous_sam")] public bool? RestrictAnonymousSam { get; init; }
    [JsonPropertyName("disable_restricted_admin")] public bool? DisableRestrictedAdmin { get; init; }
    [JsonPropertyName("run_as_ppl")] public bool? RunAsPpl { get; init; }
    [JsonPropertyName("disable_domain_creds")] public bool? DisableDomainCreds { get; init; }
    [JsonPropertyName("local_account_token_filter_policy")] public bool? LocalAccountTokenFilterPolicy { get; init; }
}

public sealed class WDigestSettings
{
    [JsonPropertyName("use_logon_credential")] public bool? UseLogonCredential { get; init; }
}

public sealed class SmbSettings
{
    [JsonPropertyName("smb1_enabled")] public bool? Smb1Enabled { get; init; }
    [JsonPropertyName("smb1_driver_start")] public int? Smb1DriverStart { get; init; }
    [JsonPropertyName("server_signing_enabled")] public bool? ServerSigningEnabled { get; init; }
    [JsonPropertyName("client_signing_required")] public bool? ClientSigningRequired { get; init; }
}

public sealed class PowerShellPolicy
{
    [JsonPropertyName("execution_policy")] public string? ExecutionPolicy { get; init; }
    [JsonPropertyName("script_block_logging")] public bool? ScriptBlockLogging { get; init; }
    [JsonPropertyName("transcription")] public bool? Transcription { get; init; }
}

public sealed class WinlogonSettings
{
    [JsonPropertyName("userinit")] public string? Userinit { get; init; }
    [JsonPropertyName("shell")] public string? Shell { get; init; }
    [JsonPropertyName("auto_admin_logon")] public bool? AutoAdminLogon { get; init; }
}

public sealed class CredentialGuardSettings
{
    [JsonPropertyName("vbs_enabled")] public bool? VbsEnabled { get; init; }
    [JsonPropertyName("lsa_cfg_flags")] public int? LsaCfgFlags { get; init; }
}

public sealed class DangerousFlag
{
    [JsonPropertyName("name")] public string Name { get; init; } = string.Empty;
    [JsonPropertyName("registry_path")] public string RegistryPath { get; init; } = string.Empty;
    [JsonPropertyName("value_name")] public string ValueName { get; init; } = string.Empty;
    [JsonPropertyName("actual_value")] public string? ActualValue { get; init; }
    [JsonPropertyName("expected_value")] public string? ExpectedValue { get; init; }
    [JsonPropertyName("severity")] public string Severity { get; init; } = "medium";
    [JsonPropertyName("description")] public string? Description { get; init; }
}
