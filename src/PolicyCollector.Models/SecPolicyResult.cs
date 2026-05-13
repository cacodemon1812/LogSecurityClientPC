using System.Text.Json.Serialization;

namespace PolicyCollector.Agent.Models;

public sealed class SecPolicyResult
{
    [JsonPropertyName("password_policy")] public PasswordPolicy? PasswordPolicy { get; init; }
    [JsonPropertyName("audit_policy")] public AuditPolicy? AuditPolicy { get; init; }
    [JsonPropertyName("user_rights")] public Dictionary<string, List<string>>? UserRights { get; init; }
    [JsonPropertyName("uac")] public UacConfig? Uac { get; init; }
    [JsonPropertyName("tls")] public TlsConfig? Tls { get; init; }
    [JsonPropertyName("rdp")] public RdpConfig? Rdp { get; init; }
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
