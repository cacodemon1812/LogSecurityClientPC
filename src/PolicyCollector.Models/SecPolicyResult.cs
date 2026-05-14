using System.Text.Json.Serialization;

namespace PolicyCollector.Agent.Models;

public sealed class SecPolicyResult
{
    [JsonPropertyName("password_policy")]  public PasswordPolicy?  PasswordPolicy  { get; init; }
    [JsonPropertyName("audit_policy")]     public AuditPolicy?     AuditPolicy     { get; init; }
    [JsonPropertyName("user_rights")]      public Dictionary<string, List<string>>? UserRights { get; init; }
    [JsonPropertyName("uac")]              public UacConfig?        Uac             { get; init; }
    [JsonPropertyName("tls")]              public TlsConfig?        Tls             { get; init; }
    [JsonPropertyName("rdp")]              public RdpConfig?        Rdp             { get; init; }
    [JsonPropertyName("security_options")] public SecurityOptions?  SecurityOptions { get; init; }
}

public sealed class SecurityOptions
{
    // NTLM / LM authentication
    [JsonPropertyName("lm_compatibility_level")]      public int?  LmCompatibilityLevel     { get; init; }
    [JsonPropertyName("no_lm_hash")]                  public bool? NoLmHash                 { get; init; }
    // Anonymous access
    [JsonPropertyName("restrict_anonymous")]          public int?  RestrictAnonymous         { get; init; }
    [JsonPropertyName("restrict_anonymous_sam")]      public bool? RestrictAnonymousSam      { get; init; }
    // WDigest
    [JsonPropertyName("wdigest_enabled")]             public bool? WdigestEnabled            { get; init; }
    // LSA Protection (PPL)
    [JsonPropertyName("lsa_ppl_enabled")]             public bool? LsaPplEnabled             { get; init; }
    // SMB signing
    [JsonPropertyName("smb_client_signing_required")] public bool? SmbClientSigningRequired  { get; init; }
    [JsonPropertyName("smb_client_signing_enabled")]  public bool? SmbClientSigningEnabled   { get; init; }
    [JsonPropertyName("smb_server_signing_required")] public bool? SmbServerSigningRequired  { get; init; }
    [JsonPropertyName("smb_server_signing_enabled")]  public bool? SmbServerSigningEnabled   { get; init; }
    // UAC extras
    [JsonPropertyName("local_account_uac_filter")]    public bool? LocalAccountUacFilter     { get; init; }
    // RDP Restricted Admin (prevents pass-the-hash via RDP)
    [JsonPropertyName("restricted_admin_disabled")]   public bool? RestrictedAdminDisabled   { get; init; }
    // VBS
    [JsonPropertyName("vbs_enabled")]                 public bool? VbsEnabled                { get; init; }
    // Credential caching
    [JsonPropertyName("disable_domain_creds")]        public bool? DisableDomainCreds        { get; init; }
}

public sealed class PasswordPolicy
{
    [JsonPropertyName("min_length")] public int MinLength { get; init; }
    [JsonPropertyName("complexity_enabled")] public bool ComplexityEnabled { get; init; }
    [JsonPropertyName("max_age_days")] public int MaxAgeDays { get; init; }
    [JsonPropertyName("min_age_days")] public int MinAgeDays { get; init; }
    [JsonPropertyName("history_count")] public int HistoryCount { get; init; }
    [JsonPropertyName("lockout_threshold")] public int LockoutThreshold { get; init; }
    [JsonPropertyName("lockout_duration_min")] public int LockoutDurationMin { get; init; }
    [JsonPropertyName("lockout_window_min")] public int LockoutWindowMin { get; init; }
    [JsonPropertyName("reversible_encryption")] public bool ReversibleEncryption { get; init; }
}

public sealed class AuditPolicy
{
    [JsonPropertyName("account_logon")] public string? AccountLogon { get; init; }
    [JsonPropertyName("account_management")] public string? AccountManagement { get; init; }
    [JsonPropertyName("detailed_tracking")] public string? DetailedTracking { get; init; }
    [JsonPropertyName("ds_access")] public string? DsAccess { get; init; }
    [JsonPropertyName("logon_logoff")] public string? LogonLogoff { get; init; }
    [JsonPropertyName("object_access")] public string? ObjectAccess { get; init; }
    [JsonPropertyName("policy_change")] public string? PolicyChange { get; init; }
    [JsonPropertyName("privilege_use")] public string? PrivilegeUse { get; init; }
    [JsonPropertyName("system")] public string? System { get; init; }
    [JsonPropertyName("subcategories")] public Dictionary<string, string>? Subcategories { get; init; }
}

public sealed class UacConfig
{
    [JsonPropertyName("enabled")] public bool Enabled { get; init; }
    [JsonPropertyName("consent_prompt_level")] public int ConsentPromptLevel { get; init; }
    [JsonPropertyName("secure_desktop")] public bool SecureDesktop { get; init; }
}

public sealed class TlsConfig
{
    [JsonPropertyName("protocols")] public TlsProtocols Protocols { get; init; } = new();
}

public sealed class TlsProtocols
{
    [JsonPropertyName("SSL_2_0")] public bool Ssl20 { get; init; }
    [JsonPropertyName("SSL_3_0")] public bool Ssl30 { get; init; }
    [JsonPropertyName("TLS_1_0")] public bool Tls10 { get; init; }
    [JsonPropertyName("TLS_1_1")] public bool Tls11 { get; init; }
    [JsonPropertyName("TLS_1_2")] public bool Tls12 { get; init; }
    [JsonPropertyName("TLS_1_3")] public bool Tls13 { get; init; }
}

public sealed class RdpConfig
{
    [JsonPropertyName("enabled")] public bool Enabled { get; init; }
    [JsonPropertyName("nla_required")] public bool NlaRequired { get; init; }
    [JsonPropertyName("port")] public int Port { get; init; } = 3389;
    [JsonPropertyName("session_timeout_min")] public int SessionTimeoutMin { get; init; }
    [JsonPropertyName("disconnect_timeout_min")] public int DisconnectTimeoutMin { get; init; }
}
