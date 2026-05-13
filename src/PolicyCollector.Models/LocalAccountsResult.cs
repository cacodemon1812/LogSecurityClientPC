using System.Text.Json.Serialization;

namespace PolicyCollector.Agent.Models;

public sealed class LocalAccountsResult
{
    [JsonPropertyName("accounts")] public List<LocalAccount> Accounts { get; init; } = [];
    [JsonPropertyName("administrators")] public List<LocalGroupMember> Administrators { get; init; } = [];
}

public sealed class LocalAccount
{
    [JsonPropertyName("name")] public string? Name { get; init; }
    [JsonPropertyName("sid")] public string? Sid { get; init; }
    [JsonPropertyName("enabled")] public bool Enabled { get; init; }
    [JsonPropertyName("password_expires")] public bool PasswordExpires { get; init; }
    [JsonPropertyName("last_logon")] public string? LastLogon { get; init; }
    [JsonPropertyName("description")] public string? Description { get; init; }
    [JsonPropertyName("is_builtin_admin")] public bool IsBuiltinAdmin { get; init; }
}

public sealed class LocalGroupMember
{
    [JsonPropertyName("name")] public string? Name { get; init; }
    [JsonPropertyName("type")] public string? Type { get; init; }
    [JsonPropertyName("is_domain")] public bool IsDomain { get; init; }
}
