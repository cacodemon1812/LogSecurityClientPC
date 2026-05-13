using System.Text.Json.Serialization;

namespace PolicyCollector.Agent.Models;

public sealed class EventLogSettings
{
    [JsonPropertyName("logs")] public List<EventLogInfo> Logs { get; init; } = [];
    [JsonPropertyName("event_forwarding_enabled")] public bool EventForwardingEnabled { get; init; }
}

public sealed class EventLogInfo
{
    [JsonPropertyName("name")] public string? Name { get; init; }
    [JsonPropertyName("enabled")] public bool Enabled { get; init; }
    [JsonPropertyName("max_size_mb")] public long MaxSizeMb { get; init; }
    [JsonPropertyName("log_mode")] public string? LogMode { get; init; }
    [JsonPropertyName("record_count")] public long RecordCount { get; init; }
}
