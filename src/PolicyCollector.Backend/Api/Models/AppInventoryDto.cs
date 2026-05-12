using System.Text.Json.Serialization;

namespace PolicyCollector.Backend.Api.Models;

public sealed record AppInventoryDto
{
    [JsonPropertyName("display_name")]
    public string DisplayName { get; init; } = string.Empty;

    [JsonPropertyName("version")]
    public string? Version { get; init; }

    [JsonPropertyName("publisher")]
    public string? Publisher { get; init; }

    // PostgreSQL COUNT returns int8 (long)
    [JsonPropertyName("machine_count")]
    public long MachineCount { get; init; }

    [JsonPropertyName("last_seen")]
    public DateTimeOffset? LastSeen { get; init; }
}
