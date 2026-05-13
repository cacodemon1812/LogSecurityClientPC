using System.Text.Json.Serialization;

namespace PolicyCollector.Agent.Models;

public sealed class LapsResult
{
    [JsonPropertyName("type")] public string? Type { get; init; }
    [JsonPropertyName("policy_configured")] public bool PolicyConfigured { get; init; }
    [JsonPropertyName("backup_directory")] public string? BackupDirectory { get; init; }
    [JsonPropertyName("password_expiry_days")] public int? PasswordExpiryDays { get; init; }
    [JsonPropertyName("admin_account_name")] public string? AdminAccountName { get; init; }
}
