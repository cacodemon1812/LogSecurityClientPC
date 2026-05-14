using System.Text.Json.Serialization;

namespace PolicyCollector.Agent.Models;

public sealed class CollectionPayload
{
    [JsonPropertyName("schema_version")]
    public string SchemaVersion { get; init; } = "1.0";

    [JsonPropertyName("collection_id")]
    public string CollectionId { get; init; } = Guid.NewGuid().ToString();

    [JsonPropertyName("collected_at")]
    public DateTimeOffset CollectedAt { get; init; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("agent_version")]
    public string AgentVersion { get; init; } = GetAgentVersion();

    [JsonPropertyName("host")]
    public HostInfo? Host { get; init; }

    [JsonPropertyName("gpo")]
    public GpoResult? Gpo { get; init; }

    [JsonPropertyName("security_policy")]
    public SecPolicyResult? SecurityPolicy { get; init; }

    [JsonPropertyName("firewall")]
    public FirewallResult? Firewall { get; init; }

    [JsonPropertyName("defender")]
    public DefenderResult? Defender { get; init; }

    [JsonPropertyName("bitlocker")]
    public List<BitLockerVolume>? BitLocker { get; init; }

    [JsonPropertyName("applications")]
    public List<AppEntry>? Applications { get; init; }

    [JsonPropertyName("appx_packages")]
    public List<AppxEntry>? AppxPackages { get; init; }

    [JsonPropertyName("services")]
    public List<ServiceEntry>? Services { get; init; }

    [JsonPropertyName("scheduled_tasks")]
    public List<TaskEntry>? ScheduledTasks { get; init; }

    [JsonPropertyName("startup_entries")]
    public List<StartupEntry>? StartupEntries { get; init; }

    [JsonPropertyName("active_directory")]
    public AdInfo? ActiveDirectory { get; init; }

    [JsonPropertyName("registry_audit")]
    public RegistryAuditResult? RegistryAudit { get; init; }

    [JsonPropertyName("patch")]
    public PatchStatus? Patch { get; init; }

    [JsonPropertyName("local_accounts")]
    public LocalAccountsResult? LocalAccounts { get; init; }

    [JsonPropertyName("shared_folders")]
    public SharedFoldersResult? SharedFolders { get; init; }

    [JsonPropertyName("hardware_security")]
    public HardwareSecurity? HardwareSecurity { get; init; }

    [JsonPropertyName("event_log_settings")]
    public EventLogSettings? EventLogSettings { get; init; }

    [JsonPropertyName("remote_access")]
    public RemoteAccessResult? RemoteAccess { get; init; }

    [JsonPropertyName("laps")]
    public LapsResult? Laps { get; init; }

    [JsonPropertyName("endpoint_protection")]
    public EndpointProtectionResult? EndpointProtection { get; init; }

    [JsonPropertyName("wifi")]
    public WiFiResult? Wifi { get; init; }

    private static string GetAgentVersion() =>
        typeof(CollectionPayload).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
}
