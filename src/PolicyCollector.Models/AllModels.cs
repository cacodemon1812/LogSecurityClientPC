using System.Text.Json.Serialization;

namespace PolicyCollector.Agent.Models;

public sealed class FirewallResult
{
    [JsonPropertyName("profiles")]        public Dictionary<string, FirewallProfile>? Profiles      { get; init; }
    [JsonPropertyName("rules_summary")]   public FirewallRulesSummary?                RulesSummary  { get; init; }
    [JsonPropertyName("rules")]           public List<FirewallRule>?                  Rules         { get; init; }
    [JsonPropertyName("listening_ports")] public List<ListeningPort>?                ListeningPorts { get; init; }
    [JsonPropertyName("risky_ports")]     public List<RiskyPort>?                    RiskyPorts    { get; init; }
}

public sealed class FirewallProfile
{
    [JsonPropertyName("enabled")] public bool Enabled { get; init; }
    [JsonPropertyName("inbound")] public string? Inbound { get; init; }
    [JsonPropertyName("outbound")] public string? Outbound { get; init; }
}

public sealed class FirewallRulesSummary
{
    [JsonPropertyName("total")] public int Total { get; init; }
    [JsonPropertyName("enabled")] public int Enabled { get; init; }
    [JsonPropertyName("inbound")] public int Inbound { get; init; }
    [JsonPropertyName("outbound")] public int Outbound { get; init; }
}

public sealed class ListeningPort
{
    [JsonPropertyName("protocol")]     public string? Protocol    { get; init; }
    [JsonPropertyName("address")]      public string? Address     { get; init; }
    [JsonPropertyName("port")]         public int     Port        { get; init; }
    [JsonPropertyName("pid")]          public int?    Pid         { get; init; }
    [JsonPropertyName("process_name")] public string? ProcessName { get; init; }
}

public sealed class RiskyPort
{
    [JsonPropertyName("port")]                  public int     Port                 { get; init; }
    [JsonPropertyName("protocol")]              public string? Protocol             { get; init; }
    [JsonPropertyName("risk_level")]            public string  RiskLevel            { get; init; } = "medium";
    [JsonPropertyName("description")]           public string? Description          { get; init; }
    [JsonPropertyName("is_listening")]          public bool    IsListening          { get; init; }
    [JsonPropertyName("has_inbound_allow_rule")]public bool    HasInboundAllowRule  { get; init; }
    [JsonPropertyName("process_name")]          public string? ProcessName          { get; init; }
}

public sealed class FirewallRule
{
    [JsonPropertyName("name")] public string? Name { get; init; }
    [JsonPropertyName("display_name")] public string? DisplayName { get; init; }
    [JsonPropertyName("direction")] public string? Direction { get; init; }
    [JsonPropertyName("action")] public string? Action { get; init; }
    [JsonPropertyName("enabled")] public bool Enabled { get; init; }
    [JsonPropertyName("protocol")] public string? Protocol { get; init; }
    [JsonPropertyName("local_port")] public string? LocalPort { get; init; }
    [JsonPropertyName("remote_port")] public string? RemotePort { get; init; }
    [JsonPropertyName("profile")] public string? Profile { get; init; }
    [JsonPropertyName("program")] public string? Program { get; init; }
}

public sealed class DefenderResult
{
    [JsonPropertyName("real_time_protection")] public bool? RealTimeProtection { get; init; }
    [JsonPropertyName("cloud_protection")] public bool? CloudProtection { get; init; }
    [JsonPropertyName("signature_version")] public string? SignatureVersion { get; init; }
    [JsonPropertyName("antivirus_enabled")] public bool? AntivirusEnabled { get; init; }
}

public sealed class BitLockerVolume
{
    [JsonPropertyName("volume")] public string? Volume { get; init; }
    [JsonPropertyName("status")] public string? Status { get; init; }
    [JsonPropertyName("encryption_method")] public string? EncryptionMethod { get; init; }
    [JsonPropertyName("protection_status")] public string? ProtectionStatus { get; init; }
}

public sealed class AppEntry
{
    [JsonPropertyName("display_name")] public string? DisplayName { get; init; }
    [JsonPropertyName("display_version")] public string? DisplayVersion { get; init; }
    [JsonPropertyName("publisher")] public string? Publisher { get; init; }
    [JsonPropertyName("install_date")] public string? InstallDate { get; init; }
    [JsonPropertyName("install_location")] public string? InstallLocation { get; init; }
    [JsonPropertyName("uninstall_string")] public string? UninstallString { get; init; }
    [JsonPropertyName("architecture")] public string? Architecture { get; init; }
    [JsonPropertyName("source")] public string? Source { get; init; }
    [JsonPropertyName("registry_hive")] public string? RegistryHive { get; init; }
    [JsonPropertyName("registry_key")] public string? RegistryKey { get; init; }
}

public sealed class AppxEntry
{
    [JsonPropertyName("name")] public string? Name { get; init; }
    [JsonPropertyName("version")] public string? Version { get; init; }
    [JsonPropertyName("publisher")] public string? Publisher { get; init; }
    [JsonPropertyName("architecture")] public string? Architecture { get; init; }
    [JsonPropertyName("install_location")] public string? InstallLocation { get; init; }
}

public sealed class ServiceEntry
{
    [JsonPropertyName("name")] public string? Name { get; init; }
    [JsonPropertyName("display_name")] public string? DisplayName { get; init; }
    [JsonPropertyName("status")] public string? Status { get; init; }
    [JsonPropertyName("startup_type")] public string? StartupType { get; init; }
    [JsonPropertyName("account")] public string? Account { get; init; }
    [JsonPropertyName("binary_path")] public string? BinaryPath { get; init; }
    [JsonPropertyName("description")] public string? Description { get; init; }
    [JsonPropertyName("pid")] public int? Pid { get; init; }
}

public sealed class TaskEntry
{
    [JsonPropertyName("task_name")] public string? TaskName { get; init; }
    [JsonPropertyName("task_path")] public string? TaskPath { get; init; }
    [JsonPropertyName("state")] public string? State { get; init; }
    [JsonPropertyName("last_run_time")] public DateTimeOffset? LastRunTime { get; init; }
    [JsonPropertyName("last_run_result")] public int? LastRunResult { get; init; }
    [JsonPropertyName("next_run_time")] public DateTimeOffset? NextRunTime { get; init; }
    [JsonPropertyName("run_as_user")] public string? RunAsUser { get; init; }
}

public sealed class StartupEntry
{
    [JsonPropertyName("name")] public string? Name { get; init; }
    [JsonPropertyName("command")] public string? Command { get; init; }
    [JsonPropertyName("location")] public string? Location { get; init; }
    [JsonPropertyName("enabled")] public bool Enabled { get; init; }
}

public sealed class EndpointProtectionResult
{
    [JsonPropertyName("antivirus_products")] public List<SecurityProduct> AntivirusProducts { get; init; } = [];
    [JsonPropertyName("firewall_products")]  public List<SecurityProduct> FirewallProducts  { get; init; } = [];
    [JsonPropertyName("kaspersky_detected")] public bool KasperskyDetected { get; init; }
    [JsonPropertyName("kaspersky")]          public KasperskyDetail? Kaspersky { get; init; }
    // Populated when Kaspersky is NOT the registered firewall — signals ops team to verify Windows Firewall manually
    [JsonPropertyName("firewall_note")]      public string? FirewallNote { get; init; }
}

public sealed class SecurityProduct
{
    [JsonPropertyName("name")]        public string? Name { get; init; }
    [JsonPropertyName("enabled")]     public bool Enabled { get; init; }
    [JsonPropertyName("up_to_date")]  public bool UpToDate { get; init; }
    // Raw hex of productState from SecurityCenter2 — useful for debugging edge cases
    [JsonPropertyName("state_hex")]   public string? StateHex { get; init; }
    [JsonPropertyName("timestamp")]   public string? Timestamp { get; init; }
}

public sealed class KasperskyDetail
{
    [JsonPropertyName("product_name")]              public string? ProductName { get; init; }
    [JsonPropertyName("version")]                   public string? Version { get; init; }
    [JsonPropertyName("install_path")]              public string? InstallPath { get; init; }
    [JsonPropertyName("av_enabled")]                public bool AvEnabled { get; init; }
    [JsonPropertyName("av_up_to_date")]             public bool AvUpToDate { get; init; }
    [JsonPropertyName("firewall_registered")]       public bool FirewallRegistered { get; init; }
    [JsonPropertyName("firewall_enabled")]          public bool FirewallEnabled { get; init; }
}

public sealed class WiFiResult
{
    [JsonPropertyName("profiles")]           public List<WiFiProfile>?    Profiles          { get; init; }
    [JsonPropertyName("active_connections")] public List<WiFiConnection>? ActiveConnections { get; init; }
    [JsonPropertyName("has_insecure_profile")]public bool                 HasInsecureProfile { get; init; }
    [JsonPropertyName("insecure_ssids")]     public List<string>?         InsecureSsids     { get; init; }
}

public sealed class WiFiProfile
{
    [JsonPropertyName("ssid")]           public string? Ssid           { get; init; }
    [JsonPropertyName("authentication")] public string? Authentication { get; init; }
    [JsonPropertyName("cipher")]         public string? Cipher         { get; init; }
    // "safe" | "medium" | "high" | "critical" | "unknown"
    [JsonPropertyName("risk_level")]     public string? RiskLevel      { get; init; }
    [JsonPropertyName("is_connected")]   public bool    IsConnected    { get; init; }
}

public sealed class WiFiConnection
{
    [JsonPropertyName("interface_alias")] public string? InterfaceAlias { get; init; }
    [JsonPropertyName("network_name")]    public string? NetworkName    { get; init; }
    [JsonPropertyName("category")]        public string? Category       { get; init; }
}
