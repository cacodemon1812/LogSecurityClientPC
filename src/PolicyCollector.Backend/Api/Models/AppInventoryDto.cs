using System.Text.Json.Serialization;

namespace PolicyCollector.Backend.Api.Models;

public sealed record AppInventoryDto(
    [property: JsonPropertyName("display_name")]    string DisplayName,
    [property: JsonPropertyName("version")]         string? Version,
    [property: JsonPropertyName("publisher")]       string? Publisher,
    [property: JsonPropertyName("machine_count")]   int MachineCount,
    [property: JsonPropertyName("last_seen")]       DateTimeOffset? LastSeen
);
