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

    private static string GetAgentVersion() =>
        typeof(CollectionPayload).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
}
