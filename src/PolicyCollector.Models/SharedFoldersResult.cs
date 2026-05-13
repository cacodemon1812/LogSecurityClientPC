using System.Text.Json.Serialization;

namespace PolicyCollector.Agent.Models;

public sealed class SharedFoldersResult
{
    [JsonPropertyName("shares")] public List<SharedFolder> Shares { get; init; } = [];
}

public sealed class SharedFolder
{
    [JsonPropertyName("name")] public string? Name { get; init; }
    [JsonPropertyName("path")] public string? Path { get; init; }
    [JsonPropertyName("description")] public string? Description { get; init; }
    [JsonPropertyName("type")] public uint Type { get; init; }
    [JsonPropertyName("max_connections")] public uint MaxConnections { get; init; }
    [JsonPropertyName("access")] public List<ShareAccess> Access { get; init; } = [];
}

public sealed class ShareAccess
{
    [JsonPropertyName("account")] public string? Account { get; init; }
    [JsonPropertyName("access_control_type")] public string? AccessControlType { get; init; }
    [JsonPropertyName("access_right")] public string? AccessRight { get; init; }
}
