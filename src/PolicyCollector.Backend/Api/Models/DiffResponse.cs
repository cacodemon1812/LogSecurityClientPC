using System.Text.Json.Serialization;

namespace PolicyCollector.Backend.Api.Models;

public sealed record DiffResponse(
    [property: JsonPropertyName("hostname")]       string Hostname,
    [property: JsonPropertyName("from_snapshot")]  Guid? FromSnapshot,
    [property: JsonPropertyName("to_snapshot")]    Guid ToSnapshot,
    [property: JsonPropertyName("from_time")]      DateTimeOffset? FromTime,
    [property: JsonPropertyName("to_time")]        DateTimeOffset ToTime,
    [property: JsonPropertyName("changes")]        IReadOnlyList<ConfigChangeDto> Changes
);

public sealed record ConfigChangeDto(
    [property: JsonPropertyName("field_path")] string FieldPath,
    [property: JsonPropertyName("old_value")]  string? OldValue,
    [property: JsonPropertyName("new_value")]  string? NewValue,
    [property: JsonPropertyName("changed_at")] DateTimeOffset ChangedAt
);
