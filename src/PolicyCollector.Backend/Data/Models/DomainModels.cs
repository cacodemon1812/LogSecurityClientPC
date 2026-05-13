namespace PolicyCollector.Backend.Data.Models;

public sealed record AppUser
{
    public int Id { get; init; }
    public string Username { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string? FullName { get; init; }
    public string PasswordHash { get; init; } = string.Empty;
    public string Role { get; init; } = "viewer";
    public bool Active { get; init; } = true;
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public DateTimeOffset? LastLogin { get; init; }
}

public sealed record PolicyRule
{
    public int Id { get; init; }
    public string RuleId { get; init; } = string.Empty;
    public string Severity { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public bool Enabled { get; init; }
}

public sealed record ViolationEntry
{
    public string RuleId { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? Expected { get; set; }
    public string? Actual { get; set; }
    public string Hostname { get; set; } = string.Empty;
}

public sealed record ConfigChange
{
    public string Hostname { get; init; } = string.Empty;
    public DateTimeOffset ChangedAt { get; init; }
    public string FieldPath { get; init; } = string.Empty;
    public string? OldValue { get; init; }
    public string? NewValue { get; init; }
}

public sealed record SnapshotRow
{
    public Guid Id { get; init; }
    public Guid CollectionId { get; init; }
    public string Hostname { get; init; } = string.Empty;
    public DateTimeOffset CollectedAt { get; init; }
    public string PayloadJson { get; init; } = string.Empty;
}

public sealed record HostRow
{
    public string Hostname { get; init; } = string.Empty;
    public string? Domain { get; init; }
    public string? OsVersion { get; init; }
    public string? AgentVersion { get; init; }
    public DateTimeOffset? LastSeen { get; init; }
    public string Status { get; init; } = string.Empty;
}

public sealed record ViolationRow
{
    public long Id { get; init; }
    public Guid SnapshotId { get; init; }
    public string Hostname { get; init; } = string.Empty;
    public DateTimeOffset DetectedAt { get; init; }
    public string RuleId { get; init; } = string.Empty;
    public string Severity { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string? Expected { get; init; }
    public string? Actual { get; init; }
    public bool Resolved { get; init; }
    public DateTimeOffset? ResolvedAt { get; init; }
}
