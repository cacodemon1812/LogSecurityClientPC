using System.Text.Json.Serialization;

namespace PolicyCollector.Agent.Models;

public sealed class HostInfo
{
    [JsonPropertyName("hostname")] public string Hostname { get; init; } = string.Empty;
    [JsonPropertyName("fqdn")] public string? Fqdn { get; init; }
    [JsonPropertyName("domain")] public string? Domain { get; init; }
    [JsonPropertyName("domain_joined")] public bool DomainJoined { get; init; }
    [JsonPropertyName("workgroup")] public string? Workgroup { get; init; }
    [JsonPropertyName("os_name")] public string OsName { get; init; } = string.Empty;
    [JsonPropertyName("os_version")] public string OsVersion { get; init; } = string.Empty;
    [JsonPropertyName("os_build")] public string OsBuild { get; init; } = string.Empty;
    [JsonPropertyName("os_edition")] public string? OsEdition { get; init; }
    [JsonPropertyName("architecture")] public string Architecture { get; init; } = "x64";
    [JsonPropertyName("locale")] public string? Locale { get; init; }
    [JsonPropertyName("timezone")] public string? Timezone { get; init; }
    [JsonPropertyName("last_boot")] public DateTimeOffset? LastBoot { get; init; }
    [JsonPropertyName("ip_addresses")] public List<string> IpAddresses { get; init; } = [];
}
