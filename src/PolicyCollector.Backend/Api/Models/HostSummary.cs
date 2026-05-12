using System.Text.Json.Serialization;

namespace PolicyCollector.Backend.Api.Models;

public sealed record HostSummary(
    [property: JsonPropertyName("hostname")]       string Hostname,
    [property: JsonPropertyName("domain")]         string? Domain,
    [property: JsonPropertyName("os_version")]     string? OsVersion,
    [property: JsonPropertyName("agent_version")]  string? AgentVersion,
    [property: JsonPropertyName("last_seen")]      DateTimeOffset? LastSeen,
    [property: JsonPropertyName("status")]         string Status,
    [property: JsonPropertyName("violation_count")] int ViolationCount
);
