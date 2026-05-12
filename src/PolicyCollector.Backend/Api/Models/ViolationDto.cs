using System.Text.Json.Serialization;

namespace PolicyCollector.Backend.Api.Models;

public sealed record ViolationDto(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("snapshot_id")] Guid SnapshotId,
    [property: JsonPropertyName("hostname")] string Hostname,
    [property: JsonPropertyName("detected_at")] DateTimeOffset DetectedAt,
    [property: JsonPropertyName("rule_id")] string RuleId,
    [property: JsonPropertyName("severity")] string Severity,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("expected")] string? Expected,
    [property: JsonPropertyName("actual")] string? Actual,
    [property: JsonPropertyName("resolved")] bool Resolved,
    [property: JsonPropertyName("resolved_at")] DateTimeOffset? ResolvedAt
);
